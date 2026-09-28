using System;
using System.IO;

namespace LiteFactoryLauncher.Models;

public static class LauncherSettings
{
    private static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteFactoryLauncher");

    private static readonly string GamePathFile = Path.Combine(SettingsDirectory, "game-path.txt");

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
}
