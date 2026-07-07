using System.Diagnostics;
using System.IO.Compression;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Pop3;
using MailKit.Search;
using MimeKit;

namespace EmailBackup.Core;

/// <summary>
///     Downloads every message from a mailbox, writes each one as a native Outlook
///     <c>.msg</c> file into a dated folder, and (optionally) packages that folder into a
///     single <c>.zip</c> named <c>{email}_{yyyy-MM-dd}.zip</c>.
/// </summary>
public sealed class MailBackupService
{
    public async Task<BackupResult> BackupAsync(
        EmailAccount account,
        ServerSettings settings,
        BackupOptions options,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new BackupResult();

        var baseName = FileNaming.BackupBaseName(account.EmailAddress, options.Timestamp);
        var workingFolder = Path.Combine(options.OutputDirectory, baseName);
        Directory.CreateDirectory(workingFolder);

        try
        {
            Report(progress, BackupPhase.Connecting, stopwatch, message: $"Connecting to {settings.Host}...");

            if (settings.Protocol == MailProtocol.Imap)
                await BackupImapAsync(account, settings, options, workingFolder, result, progress, stopwatch, ct);
            else
                await BackupPop3Async(account, settings, workingFolder, result, progress, stopwatch, ct);

            result.OutputFolder = workingFolder;

            if (options.CreateZip)
            {
                Report(progress, BackupPhase.Packaging, stopwatch,
                    done: result.TotalMessages, total: result.TotalMessages,
                    message: "Creating zip archive...");

                var zipPath = Path.Combine(options.OutputDirectory, baseName + ".zip");
                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                ZipFile.CreateFromDirectory(workingFolder, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
                result.OutputZip = zipPath;

                if (!options.KeepFolderAfterZip)
                {
                    try
                    {
                        Directory.Delete(workingFolder, recursive: true);
                        result.OutputFolder = null;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Could not delete working folder: {ex.Message}");
                    }
                }
            }

            result.Success = true;
            result.Elapsed = stopwatch.Elapsed;
            Report(progress, BackupPhase.Completed, stopwatch,
                done: result.TotalMessages, total: result.TotalMessages,
                message: $"Backup complete: {result.TotalMessages} messages from {result.TotalFolders} folder(s).");
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Backup was cancelled.";
            result.Elapsed = stopwatch.Elapsed;
            Report(progress, BackupPhase.Failed, stopwatch, message: result.ErrorMessage);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Elapsed = stopwatch.Elapsed;
            Report(progress, BackupPhase.Failed, stopwatch, message: "Error: " + ex.Message);
        }

        return result;
    }

    private static async Task BackupImapAsync(
        EmailAccount account,
        ServerSettings settings,
        BackupOptions options,
        string workingFolder,
        BackupResult result,
        IProgress<BackupProgress>? progress,
        Stopwatch stopwatch,
        CancellationToken ct)
    {
        using var client = new ImapClient { Timeout = 120000 };
        await client.ConnectAsync(settings.Host, settings.Port, settings.Security.ToMailKit(), ct);
        await client.AuthenticateAsync(account.UserName, account.Password, ct);

        Report(progress, BackupPhase.Enumerating, stopwatch, message: "Enumerating folders...");

        var folders = await CollectImapFoldersAsync(client, ct);
        if (options.IncludeFolders is { Count: > 0 })
        {
            var allow = new HashSet<string>(options.IncludeFolders, StringComparer.OrdinalIgnoreCase);
            folders = folders.Where(f => allow.Contains(f.FullName)).ToList();
        }

        var total = 0;
        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();
            if (folder.Attributes.HasFlag(FolderAttributes.NoSelect))
                continue;
            try
            {
                await folder.StatusAsync(StatusItems.Count, ct);
                total += folder.Count;
            }
            catch { /* server refused STATUS; excluded from the estimate */ }
        }

        result.TotalFolders = folders.Count(f => !f.Attributes.HasFlag(FolderAttributes.NoSelect));

        var done = 0;
        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();
            if (folder.Attributes.HasFlag(FolderAttributes.NoSelect))
                continue;

            try
            {
                await folder.OpenAsync(FolderAccess.ReadOnly, ct);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Skipped folder '{folder.FullName}': {ex.Message}");
                continue;
            }

            var targetDir = Path.Combine(workingFolder, MapFolderPath(folder));
            Directory.CreateDirectory(targetDir);

            var uids = await folder.SearchAsync(SearchQuery.All, ct);
            var indexInFolder = 0;

            foreach (var uid in uids)
            {
                ct.ThrowIfCancellationRequested();
                indexInFolder++;
                try
                {
                    var message = await folder.GetMessageAsync(uid, ct);
                    var path = Path.Combine(targetDir, BuildMessageFileName(indexInFolder, message));
                    WriteMsg(message, path);
                    result.TotalMessages++;
                }
                catch (Exception ex)
                {
                    result.FailedMessages++;
                    result.Warnings.Add($"Message {uid} in '{folder.FullName}' failed: {ex.Message}");
                }

                done++;
                ReportDownload(progress, stopwatch, folder.FullName, done, total);
            }

            await folder.CloseAsync(false, ct);
        }

        await client.DisconnectAsync(true, ct);
    }

    private static async Task BackupPop3Async(
        EmailAccount account,
        ServerSettings settings,
        string workingFolder,
        BackupResult result,
        IProgress<BackupProgress>? progress,
        Stopwatch stopwatch,
        CancellationToken ct)
    {
        using var client = new Pop3Client { Timeout = 120000 };
        await client.ConnectAsync(settings.Host, settings.Port, settings.Security.ToMailKit(), ct);
        await client.AuthenticateAsync(account.UserName, account.Password, ct);

        result.TotalFolders = 1;
        var total = client.Count;

        var targetDir = Path.Combine(workingFolder, "Inbox");
        Directory.CreateDirectory(targetDir);

        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var message = await client.GetMessageAsync(i, ct);
                var path = Path.Combine(targetDir, BuildMessageFileName(i + 1, message));
                WriteMsg(message, path);
                result.TotalMessages++;
            }
            catch (Exception ex)
            {
                result.FailedMessages++;
                result.Warnings.Add($"Message {i + 1} failed: {ex.Message}");
            }

            ReportDownload(progress, stopwatch, "Inbox", i + 1, total);
        }

        await client.DisconnectAsync(true, ct);
    }

    private static async Task<List<IMailFolder>> CollectImapFoldersAsync(ImapClient client, CancellationToken ct)
    {
        var list = new List<IMailFolder>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        list.Add(client.Inbox);
        seen.Add(client.Inbox.FullName);

        foreach (var ns in client.PersonalNamespaces)
        {
            var root = client.GetFolder(ns);
            await AddSubfoldersAsync(root, list, seen, ct);
        }

        return list;
    }

    private static async Task AddSubfoldersAsync(IMailFolder folder, List<IMailFolder> list, HashSet<string> seen, CancellationToken ct)
    {
        IList<IMailFolder> subfolders;
        try
        {
            subfolders = await folder.GetSubfoldersAsync(false, ct);
        }
        catch
        {
            return;
        }

        foreach (var sub in subfolders)
        {
            if (seen.Add(sub.FullName))
                list.Add(sub);
            await AddSubfoldersAsync(sub, list, seen, ct);
        }
    }

    private static string MapFolderPath(IMailFolder folder)
    {
        var separator = folder.DirectorySeparator == '\0' ? '/' : folder.DirectorySeparator;
        var parts = folder.FullName.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return FileNaming.SanitizeSegment(folder.FullName);

        return Path.Combine(parts.Select(p => FileNaming.SanitizeSegment(p)).ToArray());
    }

    private static string BuildMessageFileName(int index, MimeMessage message)
    {
        var subject = string.IsNullOrWhiteSpace(message.Subject) ? "(no subject)" : message.Subject;
        var datePrefix = message.Date == default ? string.Empty : message.Date.ToString("yyyyMMdd") + "-";
        return $"{index:D5}-{datePrefix}{FileNaming.SanitizeSegment(subject, 80)}.msg";
    }

    internal static void WriteMsg(MimeMessage message, string path)
    {
        using var eml = new MemoryStream();
        message.WriteTo(eml);
        eml.Position = 0;

        using var msg = File.Create(path);
        MsgKit.Converter.ConvertEmlToMsg(eml, msg);
    }

    private static void ReportDownload(IProgress<BackupProgress>? progress, Stopwatch stopwatch, string folder, int done, int total)
    {
        double? estimatedTotal = null;
        if (done > 0 && total > 0)
            estimatedTotal = stopwatch.Elapsed.TotalSeconds / done * total;

        progress?.Report(new BackupProgress
        {
            Phase = BackupPhase.Downloading,
            CurrentFolder = folder,
            MessagesDone = done,
            MessagesTotal = total,
            Elapsed = stopwatch.Elapsed,
            EstimatedTotalSeconds = estimatedTotal,
            Message = $"[{done}/{total}] {folder}"
        });
    }

    private static void Report(IProgress<BackupProgress>? progress, BackupPhase phase, Stopwatch stopwatch,
        int done = 0, int total = 0, string? message = null)
    {
        progress?.Report(new BackupProgress
        {
            Phase = phase,
            MessagesDone = done,
            MessagesTotal = total,
            Elapsed = stopwatch.Elapsed,
            Message = message
        });
    }
}
