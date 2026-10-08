using System.IO;
using System.Text.Json;
using KerioTrafficMonitor.Domain.Interfaces;
using KerioTrafficMonitor.Domain.Models;

namespace KerioTrafficMonitor.Infrastructure.Persistence;

internal sealed class JsonUserStore : IUserStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KerioTrafficMonitor", "users.json");

    public async Task<IReadOnlyList<KerioUser>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
            return [];

        await using var stream = File.OpenRead(_filePath);
        var users = await JsonSerializer.DeserializeAsync<List<KerioUser>>(stream, JsonOptions, cancellationToken);
        return users?.OrderBy(x => x.Priority).ThenBy(x => x.Username).ToList() ?? [];
    }

    public async Task SaveAsync(IReadOnlyCollection<KerioUser> users, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);

        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream,
                users.OrderBy(x => x.Priority).ThenBy(x => x.Username).ToList(),
                JsonOptions,
                cancellationToken);
        }

        File.Move(tempPath, _filePath, true);
    }
}
