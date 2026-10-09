using KerioTrafficMonitor.Application.Interfaces;
using KerioTrafficMonitor.Application.Options;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.Json;

namespace KerioTrafficMonitor.Infrastructure.Persistence;

internal sealed class JsonSettingsStore(IConfiguration configuration) : ISettingsStore
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task SaveAsync(
        KerioOptions kerio,
        MonitoringOptions monitoring,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kerio);
        ArgumentNullException.ThrowIfNull(monitoring);

        await _saveLock.WaitAsync(cancellationToken);

        var path = UserSettingsPaths.FilePath;
        var tempPath = path + ".tmp";

        try
        {
            Directory.CreateDirectory(
                UserSettingsPaths.DirectoryPath);

            var document = new
            {
                Kerio = new
                {
                    BaseUrl = kerio.BaseUrl
                },
                Monitoring = new
                {
                    IntervalSeconds = monitoring.IntervalSeconds,
                    SwitchThresholdPercent =
                        monitoring.SwitchThresholdPercent,
                    AutomaticSwitching =
                        monitoring.AutomaticSwitching
                }
            };

            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    JsonOptions,
                    cancellationToken);

                await stream.FlushAsync(cancellationToken);
            }

            // Заменяем файл целиком, чтобы не оставить
            // частично записанный JSON при аварии.
            File.Move(tempPath, path, overwrite: true);

            // Применяем новое содержимое к IConfiguration.
            if (configuration is IConfigurationRoot root)
            {
                root.Reload();
            }
            else
            {
                throw new InvalidOperationException(
                    "Конфигурация не поддерживает перезагрузку.");
            }
        }
        finally
        {
            _saveLock.Release();

            // Удаляем временный файл, если запись не завершилась.
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // Остаточный временный файл не мешает работе.
                }
            }
        }
    }
}