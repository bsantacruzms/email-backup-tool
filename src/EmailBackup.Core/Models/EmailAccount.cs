namespace EmailBackup.Core;

/// <summary>Credentials for the mailbox to back up.</summary>
public sealed class EmailAccount
{
    public string EmailAddress { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>Optional login name when it differs from the e-mail address.</summary>
    public string? UserNameOverride { get; set; }

    public string UserName =>
        string.IsNullOrWhiteSpace(UserNameOverride) ? EmailAddress : UserNameOverride!;

    /// <summary>The lower-cased domain part of the address (e.g. <c>gmail.com</c>).</summary>
    public string Domain
    {
        get
        {
            var at = EmailAddress.LastIndexOf('@');
            return at >= 0 && at < EmailAddress.Length - 1
                ? EmailAddress[(at + 1)..].Trim().ToLowerInvariant()
                : string.Empty;
        }
    }
}
