using KerioTrafficMonitor.Domain.Models;

namespace KerioTrafficMonitor.Domain.Interfaces;

public interface IKerioClient : IAsyncDisposable
{
    Task LoginAsync(string username, string password, CancellationToken cancellationToken = default);
    Task<TrafficInfo> GetTrafficInfoAsync(CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
}
