namespace KerioTrafficMonitor.Domain.Models;

public sealed record MonitoringSnapshot(
    KerioUser? CurrentUser,
    TrafficInfo? Traffic,
    IReadOnlyDictionary<Guid, KerioUserStatus> UserStatuses,
    string? Error,
    DateTimeOffset? UpdatedAt);