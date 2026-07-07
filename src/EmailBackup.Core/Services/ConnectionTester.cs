using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Pop3;

namespace EmailBackup.Core;

/// <summary>Validates that credentials work against a specific server.</summary>
public sealed class ConnectionTester
{
    public async Task<ConnectionTestResult> TestAsync(EmailAccount account, ServerSettings settings, CancellationToken ct = default)
    {
        try
        {
            if (settings.Protocol == MailProtocol.Imap)
            {
                using var client = new ImapClient { Timeout = 30000 };
                await client.ConnectAsync(settings.Host, settings.Port, settings.Security.ToMailKit(), ct);
                await client.AuthenticateAsync(account.UserName, account.Password, ct);

                await client.Inbox.OpenAsync(FolderAccess.ReadOnly, ct);
                var count = client.Inbox.Count;

                await client.DisconnectAsync(true, ct);
                return ConnectionTestResult.Ok(settings, count);
            }
            else
            {
                using var client = new Pop3Client { Timeout = 30000 };
                await client.ConnectAsync(settings.Host, settings.Port, settings.Security.ToMailKit(), ct);
                await client.AuthenticateAsync(account.UserName, account.Password, ct);

                var count = client.Count;

                await client.DisconnectAsync(true, ct);
                return ConnectionTestResult.Ok(settings, count);
            }
        }
        catch (Exception ex)
        {
            return ConnectionTestResult.Fail(ex.Message, settings);
        }
    }
}
