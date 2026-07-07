using EmailBackup.Core;
using MimeKit;
using Xunit;

namespace EmailBackup.Core.Tests;

public class MsgExportTests
{
    // The first 8 bytes of any OLE2 / Compound File Binary (.msg) file.
    private static readonly byte[] CompoundFileMagic =
        { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

    [Fact]
    public void WriteMsg_ProducesValidOutlookMsgFile()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alice Sender", "alice@example.com"));
        message.To.Add(new MailboxAddress("Bob Receiver", "bob@example.com"));
        message.Subject = "Integration test subject";
        message.Date = new DateTimeOffset(2026, 7, 6, 9, 0, 0, TimeSpan.Zero);

        var builder = new BodyBuilder
        {
            TextBody = "Hello from the plain text body.",
            HtmlBody = "<html><body><b>Hello</b> from the HTML body.</body></html>"
        };
        builder.Attachments.Add("note.txt", "attachment content"u8.ToArray());
        message.Body = builder.ToMessageBody();

        var path = Path.Combine(Path.GetTempPath(), $"ebk-test-{Guid.NewGuid():N}.msg");
        try
        {
            MailBackupService.WriteMsg(message, path);

            Assert.True(File.Exists(path), "The .msg file was not created.");
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 0, "The .msg file is empty.");
            Assert.Equal(CompoundFileMagic, bytes.Take(8).ToArray());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
