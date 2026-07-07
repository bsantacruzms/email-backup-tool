namespace EmailBackup.Core;

/// <summary>Options that control what is backed up and where the output goes.</summary>
public sealed class BackupOptions
{
    /// <summary>Directory where the dated folder and zip file are written.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>When true (default) the dated folder is also packaged into a single .zip file.</summary>
    public bool CreateZip { get; set; } = true;

    /// <summary>When true (default) the extracted .msg folder is kept next to the zip.</summary>
    public bool KeepFolderAfterZip { get; set; } = true;

    /// <summary>Optional whitelist of IMAP folder full-names. Null or empty means every folder.</summary>
    public IReadOnlyCollection<string>? IncludeFolders { get; set; }

    /// <summary>Timestamp used for the output file name. Defaults to now.</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
}
