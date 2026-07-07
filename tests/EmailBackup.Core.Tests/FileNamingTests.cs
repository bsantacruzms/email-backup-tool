using EmailBackup.Core;
using Xunit;

namespace EmailBackup.Core.Tests;

public class FileNamingTests
{
    [Fact]
    public void SanitizeSegment_ReplacesInvalidCharacters()
    {
        var result = FileNaming.SanitizeSegment("Re: hello/there?<x>");

        foreach (var invalid in Path.GetInvalidFileNameChars())
            Assert.DoesNotContain(invalid, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SanitizeSegment_BlankReturnsUnderscore(string? input)
    {
        Assert.Equal("_", FileNaming.SanitizeSegment(input));
    }

    [Fact]
    public void SanitizeSegment_TrimsToMaxLength()
    {
        var result = FileNaming.SanitizeSegment(new string('a', 500), maxLength: 80);
        Assert.Equal(80, result.Length);
    }

    [Fact]
    public void SanitizeSegment_TrimsTrailingDotsAndSpaces()
    {
        Assert.Equal("report", FileNaming.SanitizeSegment("report.  "));
    }

    [Fact]
    public void BackupBaseName_ContainsEmailAndIsoDate()
    {
        var when = new DateTimeOffset(2026, 7, 6, 10, 30, 0, TimeSpan.Zero);

        var name = FileNaming.BackupBaseName("user@example.com", when);

        Assert.Equal("user@example.com_2026-07-06", name);
    }
}
