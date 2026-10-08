using KerioTrafficMonitor.Domain.Models;

namespace KerioTrafficMonitor.Domain.Interfaces;

public interface IUserRotationService
{
    event EventHandler<MonitoringSnapshot>? SnapshotChanged;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SwitchToNextUserAsync(CancellationToken cancellationToken = default);
    Task UpdateUsersAsync(IReadOnlyCollection<KerioTrafficMonitor.Domain.Models.KerioUser> users, CancellationToken cancellationToken = default);
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
