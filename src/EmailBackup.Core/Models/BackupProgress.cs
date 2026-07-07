namespace EmailBackup.Core;

/// <summary>High level stage the backup engine is currently in.</summary>
public enum BackupPhase
{
    Connecting,
    Enumerating,
    Downloading,
    Packaging,
    Completed,
    Failed
}

/// <summary>A progress snapshot reported by <see cref="MailBackupService"/>.</summary>
public sealed class BackupProgress
{
    public BackupPhase Phase { get; init; }

    public string? CurrentFolder { get; init; }

    public int MessagesDone { get; init; }

    public int MessagesTotal { get; init; }

    /// <summary>A short human readable status line, suitable for a log view.</summary>
    public string? Message { get; init; }

    public TimeSpan Elapsed { get; init; }

    /// <summary>Estimated total run time in seconds, or null while unknown.</summary>
    public double? EstimatedTotalSeconds { get; init; }

    public double Percent =>
        MessagesTotal > 0 ? Math.Min(100.0, MessagesDone * 100.0 / MessagesTotal) : 0.0;

    /// <summary>Estimated seconds remaining, or null while unknown.</summary>
    public double? EtaSeconds =>
        EstimatedTotalSeconds is { } total ? Math.Max(0.0, total - Elapsed.TotalSeconds) : null;
}
