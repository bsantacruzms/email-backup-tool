namespace EmailBackup.Core;

/// <summary>The outcome of a backup run.</summary>
public sealed class BackupResult
{
    public bool Success { get; set; }

    /// <summary>Path to the dated folder of .msg files, or null when it was removed.</summary>
    public string? OutputFolder { get; set; }

    /// <summary>Path to the produced .zip file, or null when zipping was disabled.</summary>
    public string? OutputZip { get; set; }

    public int TotalFolders { get; set; }

    public int TotalMessages { get; set; }

    public int FailedMessages { get; set; }

    public TimeSpan Elapsed { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Non-fatal issues encountered (skipped folders, individual failed messages).</summary>
    public List<string> Warnings { get; } = new();
}
