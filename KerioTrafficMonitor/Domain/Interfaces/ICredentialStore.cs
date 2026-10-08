namespace KerioTrafficMonitor.Domain.Interfaces;

public interface ICredentialStore
{
    Task SavePasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default);

    Task<string?> GetPasswordAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task DeletePasswordAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}