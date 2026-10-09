using System.IO;

namespace KerioTrafficMonitor.Application.Options;

public static class UserSettingsPaths
{
    public static string DirectoryPath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "KerioTrafficMonitor");

    public static string FilePath =>
        Path.Combine(
            DirectoryPath,
            "settings.json");
}