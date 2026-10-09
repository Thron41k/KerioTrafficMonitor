namespace KerioTrafficMonitor.Application.Interfaces;

public interface IUpdateService
{
    string CurrentVersion { get; }

    bool IsInstalled { get; }

    Task<UpdateCheckResult> CheckForUpdatesAsync();

    Task DownloadUpdateAsync();

    void ApplyAndRestart();
}

public sealed record UpdateCheckResult(
    bool IsInstalled,
    string CurrentVersion,
    string? AvailableVersion,
    string? ReleaseNotes)
{
    public bool UpdateAvailable =>
        !string.IsNullOrWhiteSpace(AvailableVersion);
}