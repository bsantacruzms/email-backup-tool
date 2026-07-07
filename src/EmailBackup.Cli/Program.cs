using System.Text;
using EmailBackup.Core;

namespace EmailBackup.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var command = args[0].ToLowerInvariant();
        var options = ParseOptions(args.Skip(1));

        try
        {
            return command switch
            {
                "detect" => await RunDetectAsync(options),
                "backup" => await RunBackupAsync(options),
                _ => Fail($"Unknown command '{command}'.")
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static async Task<int> RunDetectAsync(Dictionary<string, string> options)
    {
        var email = Require(options, "email");
        Console.WriteLine($"Discovering settings for {email} ...");

        var discovery = new AutodiscoverService();
        var candidates = await discovery.DiscoverAsync(email);
        if (candidates.Count == 0)
        {
            Console.WriteLine("No candidate settings found.");
            return 2;
        }

        Console.WriteLine();
        Console.WriteLine("Candidates (best first):");
        foreach (var c in candidates)
            Console.WriteLine($"  {c}  [{c.Source}]");

        Console.WriteLine();
        Console.Write("Probing for a reachable server ... ");
        var best = await discovery.FindReachableAsync(candidates);
        Console.WriteLine(best is null ? "none responded." : "OK");
        if (best is not null)
            Console.WriteLine($"Recommended: {best}");

        return best is null ? 2 : 0;
    }

    private static async Task<int> RunBackupAsync(Dictionary<string, string> options)
    {
        var email = Require(options, "email");
        var account = new EmailAccount
        {
            EmailAddress = email,
            UserNameOverride = options.GetValueOrDefault("user"),
            Password = options.GetValueOrDefault("password") ?? ReadPassword($"Password for {email}: ")
        };

        var settings = await ResolveSettingsAsync(options, email);
        if (settings is null)
            return Fail("Could not determine server settings. Supply --host/--port/--protocol.");

        Console.WriteLine($"Using {settings}");

        Console.Write("Validating credentials ... ");
        var tester = new ConnectionTester();
        var test = await tester.TestAsync(account, settings);
        if (!test.Success)
        {
            Console.WriteLine("FAILED");
            Console.Error.WriteLine("Login failed: " + test.Error);
            Console.Error.WriteLine(
                "Tip: Gmail and Microsoft 365 usually require an app password (not your normal password).");
            return 3;
        }

        Console.WriteLine($"OK ({test.MailboxCount ?? 0} messages in inbox)");

        var outputDir = options.GetValueOrDefault("out") ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDir);

        var backupOptions = new BackupOptions
        {
            OutputDirectory = outputDir,
            CreateZip = !options.ContainsKey("no-zip"),
            KeepFolderAfterZip = !options.ContainsKey("no-keep")
        };

        var progress = new Progress<BackupProgress>(PrintProgress);
        var service = new MailBackupService();
        var result = await service.BackupAsync(account, settings, backupOptions, progress);

        Console.WriteLine();
        if (!result.Success)
            return Fail(result.ErrorMessage ?? "Backup failed.");

        Console.WriteLine($"Done in {result.Elapsed:hh\\:mm\\:ss}. " +
                          $"{result.TotalMessages} messages, {result.FailedMessages} failed.");
        if (result.OutputZip is not null)
            Console.WriteLine("Zip:    " + result.OutputZip);
        if (result.OutputFolder is not null)
            Console.WriteLine("Folder: " + result.OutputFolder);

        if (result.Warnings.Count > 0)
            Console.WriteLine($"({result.Warnings.Count} warning(s) - some messages/folders were skipped.)");

        return 0;
    }

    private static async Task<ServerSettings?> ResolveSettingsAsync(Dictionary<string, string> options, string email)
    {
        if (options.TryGetValue("host", out var host) && !string.IsNullOrWhiteSpace(host))
        {
            var protocol = ParseProtocol(options.GetValueOrDefault("protocol")) ?? MailProtocol.Imap;
            var security = ParseSecurity(options.GetValueOrDefault("security")) ?? SocketSecurity.Auto;
            _ = int.TryParse(options.GetValueOrDefault("port"), out var port);
            if (port <= 0)
                port = protocol == MailProtocol.Imap ? 993 : 995;

            return new ServerSettings
            {
                Protocol = protocol,
                Host = host,
                Port = port,
                Security = security,
                Source = "manual"
            };
        }

        Console.Write("Auto-detecting server settings ... ");
        var discovery = new AutodiscoverService();
        var best = await discovery.DiscoverBestAsync(email);
        Console.WriteLine(best is null ? "failed." : "OK");

        if (best is not null && options.TryGetValue("protocol", out var forced))
        {
            var wanted = ParseProtocol(forced);
            if (wanted is not null && wanted != best.Protocol)
            {
                var candidates = await discovery.DiscoverAsync(email);
                best = await discovery.FindReachableAsync(candidates.Where(c => c.Protocol == wanted)) ?? best;
            }
        }

        return best;
    }

    private static void PrintProgress(BackupProgress p)
    {
        if (p.Phase == BackupPhase.Downloading)
        {
            var eta = p.EtaSeconds is { } s ? TimeSpan.FromSeconds(s).ToString("hh\\:mm\\:ss") : "--:--:--";
            Console.Write($"\r  {p.Percent,5:0.0}%  {p.MessagesDone}/{p.MessagesTotal}  ETA {eta}   ");
        }
        else if (!string.IsNullOrEmpty(p.Message))
        {
            Console.WriteLine(p.Message);
        }
    }

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? key = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (key is not null)
                    dict[key] = string.Empty; // previous was a flag
                key = arg[2..].ToLowerInvariant();
            }
            else if (key is not null)
            {
                dict[key] = arg;
                key = null;
            }
        }

        if (key is not null)
            dict[key] = string.Empty;

        return dict;
    }

    private static MailProtocol? ParseProtocol(string? value) => value?.ToLowerInvariant() switch
    {
        "imap" => MailProtocol.Imap,
        "pop" or "pop3" => MailProtocol.Pop3,
        _ => null
    };

    private static SocketSecurity? ParseSecurity(string? value) => value?.ToLowerInvariant() switch
    {
        "ssl" or "ssl-on-connect" or "tls" => SocketSecurity.SslOnConnect,
        "starttls" => SocketSecurity.StartTls,
        "none" or "plain" => SocketSecurity.None,
        "auto" => SocketSecurity.Auto,
        _ => null
    };

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        var builder = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length--;
                    Console.Write("\b \b");
                }
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
                Console.Write('*');
            }
        }

        return builder.ToString();
    }

    private static string Require(Dictionary<string, string> options, string key)
    {
        if (options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;
        throw new ArgumentException($"Missing required option --{key}.");
    }

    private static bool IsHelp(string arg) =>
        arg is "-h" or "--help" or "help" or "/?";

    private static int Fail(string message)
    {
        Console.Error.WriteLine("Error: " + message);
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            Email Backup Tool (ebk) - back up a mailbox to Outlook .msg files in a dated zip.

            USAGE
              ebk detect --email <address>
              ebk backup --email <address> [options]

            BACKUP OPTIONS
              --email     <address>   Mailbox to back up (required).
              --password  <secret>    Password / app password. Prompted if omitted.
              --user      <login>     Login name, if different from the e-mail address.
              --out       <folder>    Output folder (default: current directory).
              --protocol  imap|pop3   Force a protocol (default: auto-detect, prefers IMAP).
              --host      <host>      Server host. If given, disables auto-detect.
              --port      <number>    Server port (default 993 IMAP / 995 POP3).
              --security  ssl|starttls|none|auto   Socket security (default: auto).
              --no-zip                Write the .msg folder only, do not create a zip.
              --no-keep               Delete the .msg folder after zipping.

            EXAMPLES
              ebk detect --email me@example.com
              ebk backup --email me@example.com --out D:\Backups
              ebk backup --email me@corp.com --host mail.corp.com --port 993 --security ssl
            """);
    }
}
