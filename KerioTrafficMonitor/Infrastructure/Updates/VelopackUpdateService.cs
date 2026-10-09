using System.Reflection;
using KerioTrafficMonitor.Application.Interfaces;
using Velopack;
using Velopack.Sources;

namespace KerioTrafficMonitor.Infrastructure.Updates;

internal sealed class VelopackUpdateService : IUpdateService
{
    private const string RepositoryUrl =
        "https://github.com/Thron41k/KerioTrafficMonitor";

    private readonly UpdateManager _manager;

    private UpdateInfo? _pendingUpdate;

    public VelopackUpdateService()
    {
        _manager = new UpdateManager(
            new GithubSource(
                RepositoryUrl,
                accessToken: null,
                prerelease: false));
    }

    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersion =>
        _manager.CurrentVersion?.ToString()
        ?? GetAssemblyVersion();

    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        _pendingUpdate = null;

        // Запуск из Visual Studio или напрямую из bin/Release
        // не является установкой Velopack.
        if (!IsInstalled)
        {
            return new UpdateCheckResult(
                IsInstalled: false,
                CurrentVersion: CurrentVersion,
                AvailableVersion: null,
                ReleaseNotes: null);
        }

        _pendingUpdate =
            await _manager.CheckForUpdatesAsync();

        return new UpdateCheckResult(
            IsInstalled: true,
            CurrentVersion: CurrentVersion,
            AvailableVersion:
                _pendingUpdate?.TargetFullRelease.Version.ToString(),
            ReleaseNotes:
                _pendingUpdate?.TargetFullRelease.NotesMarkdown);
    }

    public async Task DownloadUpdateAsync()
    {
        if (!IsInstalled)
        {
            throw new InvalidOperationException(
                "Обновление доступно только для версии, " +
                "установленной через Velopack.");
        }

        if (_pendingUpdate is null)
        {
            throw new InvalidOperationException(
                "Сначала необходимо проверить наличие обновления.");
        }

        await _manager.DownloadUpdatesAsync(
            _pendingUpdate);
    }

    public void ApplyAndRestart()
    {
        if (_pendingUpdate is null)
        {
            throw new InvalidOperationException(
                "Нет подготовленного обновления.");
        }

        // Velopack завершит текущий процесс,
        // применит обновление и запустит новую версию.
        _manager.ApplyUpdatesAndRestart(
            _pendingUpdate.TargetFullRelease);
    }

    private static string GetAssemblyVersion()
    {
        var version =
            Assembly.GetEntryAssembly()?.GetName().Version;

        return version is null
            ? "unknown"
            : $"{version.Major}.{version.Minor}." +
              $"{Math.Max(0, version.Build)}";
    }
}