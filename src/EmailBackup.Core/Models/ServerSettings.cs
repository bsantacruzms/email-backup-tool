namespace EmailBackup.Core;

/// <summary>Connection settings for an incoming mail server.</summary>
public sealed class ServerSettings
{
    public MailProtocol Protocol { get; set; } = MailProtocol.Imap;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public SocketSecurity Security { get; set; } = SocketSecurity.Auto;

    /// <summary>Human readable description of how these settings were found.</summary>
    public string Source { get; set; } = "manual";

    public ServerSettings Clone() => new()
    {
        Protocol = Protocol,
        Host = Host,
        Port = Port,
        Security = Security,
        Source = Source
    };

    public override string ToString()
        => $"{Protocol.ToString().ToUpperInvariant()} {Host}:{Port} ({Security})";
}
