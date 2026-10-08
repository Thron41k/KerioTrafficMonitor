using KerioTrafficMonitor.Domain.Models;

namespace KerioTrafficMonitor.Domain.Interfaces;

public interface IUserStore
{
    Task<IReadOnlyList<KerioUser>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyCollection<KerioUser> users, CancellationToken cancellationToken = default);
}
