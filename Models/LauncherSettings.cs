using System;
using System.IO;

namespace LiteFactoryLauncher.Models;

public static class LauncherSettings
{
    private static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteFactoryLauncher");

    private static readonly string GamePathFile = Path.Combine(SettingsDirectory, "game-path.txt");

    private static readonly string InstallDirectoryFile = Path.Combine(SettingsDirectory, "install-directory.txt");

    private static readonly string InstalledPackVersionFile = Path.Combine(SettingsDirectory, "installed-pack-version.txt");

    public static string? LoadGamePath()
    {
        if (!File.Exists(GamePathFile))
        {
            return null;
        }

        var path = File.ReadAllText(GamePathFile).Trim();
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public static void SaveGamePath(string path)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(GamePathFile, path);
    }

    public static string LoadInstallDirectory()
    {
        if (File.Exists(InstallDirectoryFile))
        {
            var path = File.ReadAllText(InstallDirectoryFile).Trim();
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightFactory");
    }

    public static void SaveInstallDirectory(string path)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(InstallDirectoryFile, path);
    }

    public static string? LoadInstalledPackVersion()
    {
        if (!File.Exists(InstalledPackVersionFile))
        {
            return null;
        }

        var version = File.ReadAllText(InstalledPackVersionFile).Trim();
        return string.IsNullOrWhiteSpace(version) ? null : version;
    }

    public static void SaveInstalledPackVersion(string version)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(InstalledPackVersionFile, version);
    }
}
