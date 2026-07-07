using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using EmailBackup.Core;
using Microsoft.Win32;

namespace EmailBackup.App;

public partial class MainWindow : Window
{
    private readonly AutodiscoverService _discovery = new();
    private readonly ConnectionTester _tester = new();
    private ServerSettings? _resolvedSettings;
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();
        OutputBox.Text = DefaultOutputDir();
        Log("Ready. Enter your email and password, then Detect settings or Start backup.");
    }

    private static string DefaultOutputDir()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.IsNullOrWhiteSpace(docs)
            ? Directory.GetCurrentDirectory()
            : Path.Combine(docs, "Email Backups");
    }

    // ---- Button handlers -------------------------------------------------

    private async void DetectButton_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailBox.Text.Trim();
        if (!IsValidEmail(email))
        {
            Warn("Please enter a valid email address.");
            return;
        }

        SetBusy(true);
        Progress.IsIndeterminate = true;
        SetStatus("Detecting server settings...");
        try
        {
            Log($"Detecting settings for {email} ...");
            var settings = await DetectAsync(email);
            if (settings is not null)
            {
                _resolvedSettings = settings;
                ApplySettingsToUi(settings);
                Log($"Detected: {settings} [{settings.Source}]");
                SetStatus("Server settings detected.");
            }
            else
            {
                DetectedText.Text = "Could not auto-detect. Please fill in Advanced settings.";
                Log("Could not auto-detect settings.");
                SetStatus("Detection failed.");
            }
        }
        catch (Exception ex)
        {
            Log("Detect error: " + ex.Message);
            SetStatus("Detection error.");
        }
        finally
        {
            Progress.IsIndeterminate = false;
            SetBusy(false);
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        var account = TryBuildAccount();
        if (account is null)
            return;

        SetBusy(true);
        Progress.IsIndeterminate = true;
        SetStatus("Testing connection...");
        try
        {
            var settings = await ResolveSettingsAsync(account.EmailAddress);
            if (settings is null)
            {
                Warn("No server settings found. Use Detect settings or fill in Advanced settings.");
                return;
            }

            ApplySettingsToUi(settings);
            Log($"Testing {settings} ...");
            var result = await _tester.TestAsync(account, settings);
            if (result.Success)
            {
                _resolvedSettings = settings;
                Log($"Connection OK. Inbox has {result.MailboxCount ?? 0} message(s).");
                SetStatus("Connection successful.");
            }
            else
            {
                Log("Connection failed: " + result.Error);
                Log("Tip: Gmail and Microsoft 365 require an app password, not your normal password.");
                SetStatus("Connection failed.");
            }
        }
        catch (Exception ex)
        {
            Log("Test error: " + ex.Message);
            SetStatus("Error.");
        }
        finally
        {
            Progress.IsIndeterminate = false;
            SetBusy(false);
        }
    }

    private async void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        var account = TryBuildAccount();
        if (account is null)
            return;

        var outDir = OutputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outDir))
        {
            Warn("Please choose an output folder.");
            return;
        }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        Progress.IsIndeterminate = true;
        Progress.Value = 0;
        SetStatus("Starting...");
        try
        {
            var settings = await ResolveSettingsAsync(account.EmailAddress);
            if (settings is null)
            {
                Warn("Could not determine server settings.");
                return;
            }

            ApplySettingsToUi(settings);
            Log($"Backing up {account.EmailAddress} via {settings} ...");
            Directory.CreateDirectory(outDir);

            var options = new BackupOptions
            {
                OutputDirectory = outDir,
                CreateZip = ZipCheck.IsChecked == true,
                KeepFolderAfterZip = KeepCheck.IsChecked == true
            };

            var progress = new Progress<BackupProgress>(OnProgress);
            var service = new MailBackupService();
            var result = await service.BackupAsync(account, settings, options, progress, _cts.Token);

            if (result.Success)
            {
                Progress.IsIndeterminate = false;
                Progress.Value = 100;
                Log($"Completed: {result.TotalMessages} message(s), {result.FailedMessages} failed, " +
                    $"in {result.Elapsed:hh\\:mm\\:ss}.");
                if (result.OutputZip is not null)
                    Log("Zip:    " + result.OutputZip);
                if (result.OutputFolder is not null)
                    Log("Folder: " + result.OutputFolder);
                SetStatus($"Backup complete - {result.TotalMessages} message(s).");
                OfferOpen(result.OutputZip ?? result.OutputFolder);
            }
            else
            {
                Log("Backup failed: " + result.ErrorMessage);
                Log("Tip: Gmail and Microsoft 365 require an app password, not your normal password.");
                SetStatus("Backup failed.");
            }

            if (result.Warnings.Count > 0)
                Log($"{result.Warnings.Count} warning(s). First: {result.Warnings[0]}");
        }
        catch (Exception ex)
        {
            Log("Backup error: " + ex.Message);
            SetStatus("Error.");
        }
        finally
        {
            Progress.IsIndeterminate = false;
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Log("Cancelling...");
        SetStatus("Cancelling...");
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose backup folder" };
        if (!string.IsNullOrWhiteSpace(OutputBox.Text) && Directory.Exists(OutputBox.Text))
            dialog.InitialDirectory = OutputBox.Text;

        if (dialog.ShowDialog(this) == true)
            OutputBox.Text = dialog.FolderName;
    }

    // ---- Progress --------------------------------------------------------

    private void OnProgress(BackupProgress p)
    {
        switch (p.Phase)
        {
            case BackupPhase.Downloading:
                Progress.IsIndeterminate = false;
                Progress.Value = p.Percent;
                var eta = p.EtaSeconds is { } s
                    ? TimeSpan.FromSeconds(s).ToString("hh\\:mm\\:ss")
                    : "--:--:--";
                SetStatus($"{p.Percent:0.0}%   {p.MessagesDone}/{p.MessagesTotal}   ETA {eta}   {p.CurrentFolder}");
                break;

            case BackupPhase.Packaging:
                Progress.IsIndeterminate = true;
                if (!string.IsNullOrEmpty(p.Message))
                    Log(p.Message);
                SetStatus("Packaging zip...");
                break;

            case BackupPhase.Connecting:
            case BackupPhase.Enumerating:
                Progress.IsIndeterminate = true;
                if (!string.IsNullOrEmpty(p.Message))
                {
                    Log(p.Message);
                    SetStatus(p.Message);
                }
                break;

            default:
                if (!string.IsNullOrEmpty(p.Message))
                    Log(p.Message);
                break;
        }
    }

    // ---- Helpers ---------------------------------------------------------

    private async Task<ServerSettings?> DetectAsync(string email)
    {
        var wanted = ProtocolFromUi();
        IEnumerable<ServerSettings> list = await _discovery.DiscoverAsync(email);
        if (wanted is not null)
            list = list.Where(c => c.Protocol == wanted);

        var candidates = list.ToList();
        var best = await _discovery.FindReachableAsync(candidates);
        return best ?? candidates.FirstOrDefault();
    }

    private async Task<ServerSettings?> ResolveSettingsAsync(string email)
    {
        var manual = GetManualSettings();
        if (manual is not null)
            return manual;
        if (_resolvedSettings is not null)
            return _resolvedSettings;

        _resolvedSettings = await DetectAsync(email);
        return _resolvedSettings;
    }

    private EmailAccount? TryBuildAccount(bool requirePassword = true)
    {
        var email = EmailBox.Text.Trim();
        if (!IsValidEmail(email))
        {
            Warn("Please enter a valid email address.");
            return null;
        }

        var password = PasswordBox.Password;
        if (requirePassword && string.IsNullOrEmpty(password))
        {
            Warn("Please enter your password or app password.");
            return null;
        }

        return new EmailAccount
        {
            EmailAddress = email,
            Password = password,
            UserNameOverride = string.IsNullOrWhiteSpace(UserBox.Text) ? null : UserBox.Text.Trim()
        };
    }

    private ServerSettings? GetManualSettings()
    {
        var host = HostBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
            return null;

        var protocol = ProtocolFromUi() ?? MailProtocol.Imap;
        var security = SecurityFromUi();
        _ = int.TryParse(PortBox.Text.Trim(), out var port);
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

    private void ApplySettingsToUi(ServerSettings s)
    {
        ProtocolBox.SelectedIndex = s.Protocol == MailProtocol.Imap ? 1 : 2;
        HostBox.Text = s.Host;
        PortBox.Text = s.Port.ToString();
        SecurityBox.SelectedIndex = s.Security switch
        {
            SocketSecurity.SslOnConnect => 1,
            SocketSecurity.StartTls => 2,
            SocketSecurity.None => 3,
            _ => 0
        };
        DetectedText.Text = $"Using {s}  [{s.Source}]";
    }

    private MailProtocol? ProtocolFromUi() => ProtocolBox.SelectedIndex switch
    {
        1 => MailProtocol.Imap,
        2 => MailProtocol.Pop3,
        _ => null
    };

    private SocketSecurity SecurityFromUi() => SecurityBox.SelectedIndex switch
    {
        1 => SocketSecurity.SslOnConnect,
        2 => SocketSecurity.StartTls,
        3 => SocketSecurity.None,
        _ => SocketSecurity.Auto
    };

    private void OfferOpen(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        var answer = MessageBox.Show(this, "Backup complete. Open the output location now?",
            "Email Backup Tool", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""));
        }
        catch (Exception ex)
        {
            Log("Could not open location: " + ex.Message);
        }
    }

    private void SetBusy(bool busy)
    {
        EmailBox.IsEnabled = !busy;
        PasswordBox.IsEnabled = !busy;
        ProtocolBox.IsEnabled = !busy;
        UserBox.IsEnabled = !busy;
        HostBox.IsEnabled = !busy;
        PortBox.IsEnabled = !busy;
        SecurityBox.IsEnabled = !busy;
        OutputBox.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        ZipCheck.IsEnabled = !busy;
        KeepCheck.IsEnabled = !busy;
        DetectButton.IsEnabled = !busy;
        TestButton.IsEnabled = !busy;
        BackupButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void Log(string message)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Email Backup Tool", MessageBoxButton.OK, MessageBoxImage.Warning);

    private static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email) && email.Contains('@') && email.IndexOf('@') < email.Length - 1;
}
