using KerioTrafficMonitor.Domain.Interfaces;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KerioTrafficMonitor.Infrastructure.Persistence;

public sealed class DpapiCredentialStore : ICredentialStore
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("KerioTrafficMonitor.Password.v1");

    private readonly string _directory;

    public DpapiCredentialStore()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "KerioTrafficMonitor",
            "credentials");

        Directory.CreateDirectory(_directory);
    }

    public async Task SavePasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var plainBytes = Encoding.UTF8.GetBytes(password);

        var encryptedBytes = ProtectedData.Protect(
            plainBytes,
            Entropy,
            DataProtectionScope.CurrentUser);

        var path = GetPath(userId);

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            await File.WriteAllBytesAsync(
                tempPath,
                encryptedBytes,
                cancellationToken);

            File.Move(
                tempPath,
                path,
                overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public async Task<string?> GetPasswordAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(userId);

        if (!File.Exists(path))
            return null;

        var encryptedBytes =
            await File.ReadAllBytesAsync(
                path,
                cancellationToken);

        try
        {
            var plainBytes = ProtectedData.Unprotect(
                encryptedBytes,
                Entropy,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public Task DeletePasswordAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(userId);

        TryDelete(path);

        return Task.CompletedTask;
    }

    private string GetPath(Guid userId)
    {
        return Path.Combine(
            _directory,
            $"{userId:N}.bin");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Не позволяем ошибке очистки временного файла
            // скрыть исходную ошибку.
        }
    }
}