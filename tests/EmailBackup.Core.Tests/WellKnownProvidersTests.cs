using EmailBackup.Core;
using Xunit;

namespace EmailBackup.Core.Tests;

public class WellKnownProvidersTests
{
    [Fact]
    public void Gmail_ReturnsImapFirst()
    {
        var settings = WellKnownProviders.ForDomain("gmail.com");

        Assert.NotEmpty(settings);
        Assert.Equal(MailProtocol.Imap, settings[0].Protocol);
        Assert.Equal("imap.gmail.com", settings[0].Host);
        Assert.Equal(993, settings[0].Port);
        Assert.Equal(SocketSecurity.SslOnConnect, settings[0].Security);
    }

    [Fact]
    public void OutlookDomains_MapToOffice365()
    {
        var settings = WellKnownProviders.ForDomain("hotmail.com");

        Assert.Contains(settings, s => s.Host == "outlook.office365.com" && s.Protocol == MailProtocol.Imap);
    }

    [Fact]
    public void UnknownDomain_ReturnsEmpty()
    {
        Assert.Empty(WellKnownProviders.ForDomain("some-random-domain-xyz.test"));
    }
}
