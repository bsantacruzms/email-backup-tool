namespace EmailBackup.Core;

/// <summary>Result of validating credentials against a mail server.</summary>
public sealed class ConnectionTestResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public ServerSettings? Settings { get; init; }

    /// <summary>Number of messages in the inbox when the probe succeeded.</summary>
    public int? MailboxCount { get; init; }

    public static ConnectionTestResult Ok(ServerSettings settings, int? count) =>
        new() { Success = true, Settings = settings, MailboxCount = count };

    public static ConnectionTestResult Fail(string error, ServerSettings? settings = null) =>
        new() { Success = false, Error = error, Settings = settings };
}
