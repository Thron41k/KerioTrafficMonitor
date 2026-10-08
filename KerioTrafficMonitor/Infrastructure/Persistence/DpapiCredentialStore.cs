using System.IO;
using System.Security.Cryptography;
using System.Text;
using KerioTrafficMonitor.Domain.Interfaces;

namespace KerioTrafficMonitor.Infrastructure.Persistence;

internal sealed class DpapiCredentialStore : ICredentialStore
{
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KerioTrafficMonitor", "credentials");

    public Task<string?> GetPasswordAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var path = GetPath(userId);
        if (!File.Exists(path))
            return Task.FromResult<string?>(null);

        var encrypted = File.ReadAllBytes(path);
        var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Task.FromResult<string?>(Encoding.UTF8.GetString(plain));
    }

    public async Task SetPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser);

        await File.WriteAllBytesAsync(GetPath(userId), encrypted, cancellationToken);
    }

    public Task DeletePasswordAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var path = GetPath(userId);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    private string GetPath(Guid userId) => Path.Combine(_directory, $"{userId:N}.bin");
}
