using System.Text;

namespace EmailBackup.Core;

/// <summary>Helpers that turn e-mail data into safe file and folder names.</summary>
public static class FileNaming
{
    private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    /// <summary>Replaces characters that are illegal in a file name and trims the length.</summary>
    public static string SanitizeSegment(string? name, int maxLength = 120)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "_";

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(c < 32 || Array.IndexOf(Invalid, c) >= 0 ? '_' : c);

        var cleaned = sb.ToString().Trim().TrimEnd('.', ' ');
        if (cleaned.Length == 0)
            cleaned = "_";

        return cleaned.Length > maxLength ? cleaned[..maxLength] : cleaned;
    }

    /// <summary>Builds the base name for a backup, e.g. <c>user@example.com_2026-07-06</c>.</summary>
    public static string BackupBaseName(string email, DateTimeOffset when)
        => $"{SanitizeSegment(email, 80)}_{when:yyyy-MM-dd}";
}
