namespace KerioTrafficMonitor.Domain.Interfaces;

public interface ICredentialStore
{
    Task<string?> GetPasswordAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SetPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default);
    Task DeletePasswordAsync(Guid userId, CancellationToken cancellationToken = default);
}
