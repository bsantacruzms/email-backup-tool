namespace EmailBackup.Core;

/// <summary>
///     A small built-in table of settings for the most common consumer mail providers.
///     Used as the first (offline) layer of auto-discovery.
/// </summary>
internal static class WellKnownProviders
{
    private static ServerSettings Imap(string host, string source) => new()
    {
        Protocol = MailProtocol.Imap,
        Host = host,
        Port = 993,
        Security = SocketSecurity.SslOnConnect,
        Source = source
    };

    private static ServerSettings Pop(string host, string source) => new()
    {
        Protocol = MailProtocol.Pop3,
        Host = host,
        Port = 995,
        Security = SocketSecurity.SslOnConnect,
        Source = source
    };

    /// <summary>Returns known settings for the given domain, best (IMAP) first.</summary>
    public static IReadOnlyList<ServerSettings> ForDomain(string domain)
    {
        const string src = "known-provider";
        switch (domain)
        {
            case "gmail.com":
            case "googlemail.com":
                return new[] { Imap("imap.gmail.com", src), Pop("pop.gmail.com", src) };

            case "outlook.com":
            case "hotmail.com":
            case "hotmail.co.uk":
            case "live.com":
            case "live.co.uk":
            case "msn.com":
            case "passport.com":
                return new[] { Imap("outlook.office365.com", src), Pop("outlook.office365.com", src) };

            case "office365.com":
                return new[] { Imap("outlook.office365.com", src) };

            case "yahoo.com":
            case "yahoo.co.uk":
            case "yahoo.ca":
            case "ymail.com":
            case "rocketmail.com":
                return new[] { Imap("imap.mail.yahoo.com", src), Pop("pop.mail.yahoo.com", src) };

            case "aol.com":
                return new[] { Imap("imap.aol.com", src), Pop("pop.aol.com", src) };

            case "icloud.com":
            case "me.com":
            case "mac.com":
                return new[] { Imap("imap.mail.me.com", src) };

            case "gmx.com":
            case "gmx.net":
            case "gmx.de":
                return new[] { Imap("imap.gmx.com", src), Pop("pop.gmx.com", src) };

            case "zoho.com":
                return new[] { Imap("imap.zoho.com", src), Pop("pop.zoho.com", src) };

            case "fastmail.com":
            case "fastmail.fm":
                return new[] { Imap("imap.fastmail.com", src), Pop("pop.fastmail.com", src) };

            case "yandex.com":
            case "yandex.ru":
                return new[] { Imap("imap.yandex.com", src), Pop("pop.yandex.com", src) };

            default:
                return Array.Empty<ServerSettings>();
        }
    }
}
