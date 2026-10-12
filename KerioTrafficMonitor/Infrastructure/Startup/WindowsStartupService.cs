using System.IO;
using Microsoft.Win32;
using KerioTrafficMonitor.Application.Interfaces;

namespace KerioTrafficMonitor.Infrastructure.Startup;

/// <summary>
/// Управляет автозапуском через HKCU Run. Не требует прав администратора.
/// Для установки Velopack использует стабильный exe в корне установки,
/// чтобы обновление приложения не оставляло устаревший путь в реестре.
/// </summary>
internal sealed class WindowsStartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KerioTrafficMonitor";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        var registeredCommand = key?.GetValue(ValueName) as string;

        if (string.IsNullOrWhiteSpace(registeredCommand))
            return false;

        return string.Equals(
            NormalizeCommand(registeredCommand),
            NormalizeCommand(BuildStartupCommand()),
            StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using var existingKey = Registry.CurrentUser.OpenSubKey(
                RunKeyPath,
                writable: true);

            existingKey?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(
            RunKeyPath,
            writable: true);

        if (key is null)
        {
            throw new InvalidOperationException(
                "Не удалось открыть раздел автозагрузки Windows.");
        }

        key.SetValue(
            ValueName,
            BuildStartupCommand(),
            RegistryValueKind.String);
    }

    private static string BuildStartupCommand()
    {
        var executablePath = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException(
                "Не удалось определить путь к исполняемому файлу приложения.");
        }

        var executableName = Path.GetFileName(executablePath);

        // Velopack помещает приложение в подпапку current, а рядом с ней
        // хранит стабильный exe-лаунчер и Update.exe. Ищем их вверх по дереву.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var updatePath = Path.Combine(directory.FullName, "Update.exe");

            if (!File.Exists(updatePath))
                continue;

            var stableExecutablePath = Path.Combine(
                directory.FullName,
                executableName);

            if (File.Exists(stableExecutablePath))
            {
                return Quote(stableExecutablePath);
            }

            // Совместимость с компоновкой Velopack без корневого exe-лаунчера.
            return $"{Quote(updatePath)} start {Quote(executableName)}";
        }

        // Для запуска из Visual Studio или обычной portable-публикации.
        return Quote(Path.GetFullPath(executablePath));
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static string NormalizeCommand(string value) => value.Trim();
}
