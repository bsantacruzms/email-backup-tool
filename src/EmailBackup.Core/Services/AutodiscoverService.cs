using System.Xml.Linq;
using DnsClient;
using MailKit.Net.Imap;
using MailKit.Net.Pop3;

namespace EmailBackup.Core;

/// <summary>
///     Works out the IMAP/POP settings for an e-mail address using several layers:
///     a built-in provider table, Mozilla/ISP autoconfig, DNS SRV records and finally
///     common host-name guesses. Guessed settings can be confirmed with
///     <see cref="FindReachableAsync"/>.
/// </summary>
public sealed class AutodiscoverService
{
    private readonly HttpClient _http;

    public AutodiscoverService(HttpClient? http = null)
    {
        if (http is not null)
        {
            _http = http;
        }
        else
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("EmailBackupTool/0.1 (+autoconfig)");
        }
    }

    /// <summary>Returns candidate settings ordered best-first (IMAP over POP, TLS first).</summary>
    public async Task<IReadOnlyList<ServerSettings>> DiscoverAsync(string email, CancellationToken ct = default)
    {
        var domain = DomainOf(email);
        var results = new List<ServerSettings>();

        void AddUnique(ServerSettings? s)
        {
            if (s is null || string.IsNullOrWhiteSpace(s.Host))
                return;
            if (results.Any(r => r.Protocol == s.Protocol
                                 && r.Host.Equals(s.Host, StringComparison.OrdinalIgnoreCase)
                                 && r.Port == s.Port))
                return;
            results.Add(s);
        }

        foreach (var s in WellKnownProviders.ForDomain(domain))
            AddUnique(s);

        try
        {
            foreach (var s in await FromAutoconfigAsync(domain, email, ct))
                AddUnique(s);
        }
        catch { /* offline / not published */ }

        try
        {
            foreach (var s in await FromSrvAsync(domain, ct))
                AddUnique(s);
        }
        catch { /* no SRV records */ }

        foreach (var s in GuessHosts(domain))
            AddUnique(s);

        return results
            .OrderBy(r => r.Protocol == MailProtocol.Imap ? 0 : 1)
            .ThenBy(r => r.Security == SocketSecurity.SslOnConnect ? 0 : 1)
            .ThenBy(r => r.Source == "known-provider" ? 0 : 1)
            .ToList();
    }

    /// <summary>Discovers candidates and returns the first one that accepts a TLS connection.</summary>
    public async Task<ServerSettings?> DiscoverBestAsync(string email, CancellationToken ct = default)
    {
        var candidates = await DiscoverAsync(email, ct);
        return await FindReachableAsync(candidates, ct);
    }

    /// <summary>Returns the first candidate the server actually answers on (no authentication).</summary>
    public async Task<ServerSettings?> FindReachableAsync(IEnumerable<ServerSettings> candidates, CancellationToken ct = default)
    {
        foreach (var s in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsReachableAsync(s, ct))
                return s;
        }

        return null;
    }

    private static async Task<bool> IsReachableAsync(ServerSettings s, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));

            if (s.Protocol == MailProtocol.Imap)
            {
                using var client = new ImapClient { Timeout = 8000 };
                await client.ConnectAsync(s.Host, s.Port, s.Security.ToMailKit(), cts.Token);
                await client.DisconnectAsync(true, CancellationToken.None);
            }
            else
            {
                using var client = new Pop3Client { Timeout = 8000 };
                await client.ConnectAsync(s.Host, s.Port, s.Security.ToMailKit(), cts.Token);
                await client.DisconnectAsync(true, CancellationToken.None);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<ServerSettings>> FromAutoconfigAsync(string domain, string email, CancellationToken ct)
    {
        var urls = new[]
        {
            $"https://autoconfig.thunderbird.net/v1.1/{domain}",
            $"https://autoconfig.{domain}/mail/config-v1.1.xml?emailaddress={Uri.EscapeDataString(email)}",
            $"https://{domain}/.well-known/autoconfig/mail/config-v1.1.xml?emailaddress={Uri.EscapeDataString(email)}"
        };

        foreach (var url in urls)
        {
            try
            {
                using var resp = await _http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode)
                    continue;

                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                var parsed = ParseAutoconfig(stream);
                if (parsed.Count > 0)
                    return parsed;
            }
            catch { /* try next url */ }
        }

        return Array.Empty<ServerSettings>();
    }

    private static List<ServerSettings> ParseAutoconfig(Stream xml)
    {
        var list = new List<ServerSettings>();
        var doc = XDocument.Load(xml);

        foreach (var server in doc.Descendants("incomingServer"))
        {
            var type = (string?)server.Attribute("type");
            var host = (string?)server.Element("hostname");
            var portText = (string?)server.Element("port");
            var socket = (string?)server.Element("socketType");

            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(host))
                continue;

            var protocol = type.Trim().ToLowerInvariant() switch
            {
                "imap" => MailProtocol.Imap,
                "pop3" => MailProtocol.Pop3,
                _ => (MailProtocol?)null
            };
            if (protocol is null)
                continue;

            var security = (socket ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "SSL" => SocketSecurity.SslOnConnect,
                "STARTTLS" => SocketSecurity.StartTls,
                "PLAIN" => SocketSecurity.None,
                _ => SocketSecurity.Auto
            };

            _ = int.TryParse(portText, out var port);
            list.Add(new ServerSettings
            {
                Protocol = protocol.Value,
                Host = host.Trim(),
                Port = port > 0 ? port : DefaultPort(protocol.Value, security),
                Security = security,
                Source = "autoconfig"
            });
        }

        return list;
    }

    private static async Task<IReadOnlyList<ServerSettings>> FromSrvAsync(string domain, CancellationToken ct)
    {
        var found = new List<ServerSettings>();
        var lookup = new LookupClient();

        async Task Query(string service, MailProtocol protocol, SocketSecurity security)
        {
            try
            {
                var response = await lookup.QueryAsync(service + domain, QueryType.SRV, cancellationToken: ct);
                foreach (var srv in response.Answers.SrvRecords().OrderBy(r => r.Priority))
                {
                    var host = srv.Target.Value.TrimEnd('.');
                    if (string.IsNullOrEmpty(host) || host == ".")
                        continue;

                    found.Add(new ServerSettings
                    {
                        Protocol = protocol,
                        Host = host,
                        Port = srv.Port,
                        Security = security,
                        Source = "dns-srv"
                    });
                }
            }
            catch { /* ignore this record type */ }
        }

        await Query("_imaps._tcp.", MailProtocol.Imap, SocketSecurity.SslOnConnect);
        await Query("_imap._tcp.", MailProtocol.Imap, SocketSecurity.StartTls);
        await Query("_pop3s._tcp.", MailProtocol.Pop3, SocketSecurity.SslOnConnect);
        await Query("_pop3._tcp.", MailProtocol.Pop3, SocketSecurity.StartTls);

        return found;
    }

    private static IEnumerable<ServerSettings> GuessHosts(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            yield break;

        var imapHosts = new[] { $"imap.{domain}", $"mail.{domain}", $"imap.mail.{domain}", domain };
        foreach (var host in imapHosts)
            yield return new ServerSettings
            {
                Protocol = MailProtocol.Imap,
                Host = host,
                Port = 993,
                Security = SocketSecurity.SslOnConnect,
                Source = "guess"
            };

        var popHosts = new[] { $"pop.{domain}", $"pop3.{domain}", $"mail.{domain}" };
        foreach (var host in popHosts)
            yield return new ServerSettings
            {
                Protocol = MailProtocol.Pop3,
                Host = host,
                Port = 995,
                Security = SocketSecurity.SslOnConnect,
                Source = "guess"
            };
    }

    private static int DefaultPort(MailProtocol protocol, SocketSecurity security) => protocol switch
    {
        MailProtocol.Imap => security == SocketSecurity.StartTls ? 143 : 993,
        _ => security == SocketSecurity.StartTls ? 110 : 995
    };

    private static string DomainOf(string email)
    {
        var at = email.LastIndexOf('@');
        return at >= 0 && at < email.Length - 1
            ? email[(at + 1)..].Trim().ToLowerInvariant()
            : email.Trim().ToLowerInvariant();
    }
}
