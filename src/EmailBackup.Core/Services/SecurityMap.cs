using MailKit.Security;

namespace EmailBackup.Core;

internal static class SecurityMap
{
    public static SecureSocketOptions ToMailKit(this SocketSecurity security) => security switch
    {
        SocketSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SocketSecurity.StartTls => SecureSocketOptions.StartTls,
        SocketSecurity.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto
    };
}
