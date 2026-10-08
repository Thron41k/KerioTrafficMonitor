namespace KerioTrafficMonitor.Domain.Models;

public enum KerioUserStatus
{
    Waiting,
    Active,
    LimitReached,
    AuthenticationFailed,
    Error,
    Disabled
}
