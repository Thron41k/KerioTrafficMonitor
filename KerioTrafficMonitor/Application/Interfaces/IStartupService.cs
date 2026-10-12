namespace KerioTrafficMonitor.Application.Interfaces;

/// <summary>
/// Управляет запуском приложения при входе текущего пользователя в Windows.
/// </summary>
public interface IStartupService
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}
