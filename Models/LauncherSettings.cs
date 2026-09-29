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

    private static readonly string MaxRamMbFile = Path.Combine(SettingsDirectory, "max-ram-mb.txt");

    private static readonly string JavaModeFile = Path.Combine(SettingsDirectory, "java-mode.txt");

    private static readonly string CustomJavaPathFile = Path.Combine(SettingsDirectory, "custom-java-path.txt");

    public static string SettingsPath => SettingsDirectory;

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

    public static int LoadMaxRamMb()
    {
        if (File.Exists(MaxRamMbFile) &&
            int.TryParse(File.ReadAllText(MaxRamMbFile).Trim(), out var value))
        {
            return Math.Clamp(value, 2048, 16384);
        }

        return 4096;
    }

    public static void SaveMaxRamMb(int maxRamMb)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(MaxRamMbFile, Math.Clamp(maxRamMb, 2048, 16384).ToString());
    }

    public static string LoadJavaMode()
    {
        if (File.Exists(JavaModeFile))
        {
            var mode = File.ReadAllText(JavaModeFile).Trim();
            if (string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase))
            {
                return "Custom";
            }
        }

        return "Automatic";
    }

    public static void SaveJavaMode(string mode)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(JavaModeFile, string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase) ? "Custom" : "Automatic");
    }

    public static string? LoadCustomJavaPath()
    {
        if (!File.Exists(CustomJavaPathFile))
        {
            return null;
        }

        var path = File.ReadAllText(CustomJavaPathFile).Trim();
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public static void SaveCustomJavaPath(string path)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(CustomJavaPathFile, path);
    }
}
