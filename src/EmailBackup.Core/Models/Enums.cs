namespace EmailBackup.Core;

/// <summary>The mail retrieval protocol used to read a mailbox.</summary>
public enum MailProtocol
{
    /// <summary>IMAP - keeps the full folder structure (recommended).</summary>
    Imap,

    /// <summary>POP3 - downloads the inbox only.</summary>
    Pop3
}

/// <summary>How the socket connection to the mail server is secured.</summary>
public enum SocketSecurity
{
    /// <summary>Let the client negotiate the most secure option available.</summary>
    Auto,

    /// <summary>Implicit TLS from the moment the socket connects (e.g. port 993/995).</summary>
    SslOnConnect,

    /// <summary>Connect in the clear then upgrade to TLS via STARTTLS (e.g. port 143/110).</summary>
    StartTls,

    /// <summary>No transport encryption (not recommended).</summary>
    None
}
