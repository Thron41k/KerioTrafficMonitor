using KerioTrafficMonitor.Application.Options;

namespace KerioTrafficMonitor.Application.Interfaces;

public interface ISettingsStore
{
    Task SaveAsync(
        KerioOptions kerio,
        MonitoringOptions monitoring,
        CancellationToken cancellationToken = default);
}