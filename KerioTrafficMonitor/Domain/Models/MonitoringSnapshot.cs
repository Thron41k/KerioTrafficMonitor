namespace KerioTrafficMonitor.Domain.Models;

public sealed record MonitoringSnapshot(
    KerioUser? CurrentUser,
    TrafficInfo? Traffic,
    KerioUserStatus? Status,
    string? Error,
    DateTimeOffset? UpdatedAt);
