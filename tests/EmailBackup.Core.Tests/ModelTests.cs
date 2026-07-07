using EmailBackup.Core;
using Xunit;

namespace EmailBackup.Core.Tests;

public class ModelTests
{
    [Theory]
    [InlineData("user@example.com", "example.com")]
    [InlineData("First.Last@Sub.Domain.CO.UK", "sub.domain.co.uk")]
    [InlineData("no-at-sign", "")]
    public void EmailAccount_Domain_IsParsedAndLowercased(string email, string expected)
    {
        var account = new EmailAccount { EmailAddress = email };
        Assert.Equal(expected, account.Domain);
    }

    [Fact]
    public void EmailAccount_UserName_DefaultsToEmailButHonorsOverride()
    {
        var account = new EmailAccount { EmailAddress = "user@example.com" };
        Assert.Equal("user@example.com", account.UserName);

        account.UserNameOverride = "corp\\user";
        Assert.Equal("corp\\user", account.UserName);
    }

    [Fact]
    public void ServerSettings_ToString_IsReadable()
    {
        var settings = new ServerSettings
        {
            Protocol = MailProtocol.Imap,
            Host = "imap.example.com",
            Port = 993,
            Security = SocketSecurity.SslOnConnect
        };

        Assert.Equal("IMAP imap.example.com:993 (SslOnConnect)", settings.ToString());
    }

    [Fact]
    public void ServerSettings_Clone_CopiesAllFields()
    {
        var original = new ServerSettings
        {
            Protocol = MailProtocol.Pop3,
            Host = "pop.example.com",
            Port = 995,
            Security = SocketSecurity.StartTls,
            Source = "unit-test"
        };

        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.Equal(original.Protocol, clone.Protocol);
        Assert.Equal(original.Host, clone.Host);
        Assert.Equal(original.Port, clone.Port);
        Assert.Equal(original.Security, clone.Security);
        Assert.Equal(original.Source, clone.Source);
    }

    [Fact]
    public void BackupProgress_ComputesPercentAndEta()
    {
        var progress = new BackupProgress
        {
            Phase = BackupPhase.Downloading,
            MessagesDone = 5,
            MessagesTotal = 10,
            Elapsed = TimeSpan.FromSeconds(40),
            EstimatedTotalSeconds = 100
        };

        Assert.Equal(50.0, progress.Percent, 3);
        Assert.Equal(60.0, progress.EtaSeconds!.Value, 3);
    }

    [Fact]
    public void BackupProgress_WithNoMessages_PercentIsZero()
    {
        var progress = new BackupProgress { MessagesTotal = 0, MessagesDone = 0 };
        Assert.Equal(0.0, progress.Percent, 3);
        Assert.Null(progress.EtaSeconds);
    }
}
