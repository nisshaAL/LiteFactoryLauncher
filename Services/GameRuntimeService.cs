using LiteFactoryLauncher.Models;
using System.Linq;
using System.Threading.Tasks;

namespace LiteFactoryLauncher.Services;

public sealed class GameRuntimeService
{
    private readonly JavaDetectionService _javaDetectionService = new();
    private readonly ManagedJavaService _managedJavaService = new();
    private readonly MinecraftRuntimeService _minecraftRuntimeService = new();
    private readonly ForgeRuntimeService _forgeRuntimeService = new();

    public async Task<GameRuntimeStatus> GetRuntimeStatusAsync(string minecraftVersion, string forgeVersion)
    {
        var javaRuntimes = await _javaDetectionService.DetectJavaRuntimesAsync();
        var customJava = await GetCompatibleCustomJavaAsync();
        var compatibleSystemJava = customJava ?? javaRuntimes.FirstOrDefault(runtime => runtime.IsCompatible);
        var compatibleManagedJava = compatibleSystemJava == null
            ? await _managedJavaService.GetInstalledRuntimeAsync()
            : null;
        var minecraftInstalled = await _minecraftRuntimeService.ValidateInstalledAsync(minecraftVersion);
        var forgeInstalled = minecraftInstalled &&
                             await _forgeRuntimeService.ValidateInstalledAsync(minecraftVersion, forgeVersion);

        var status = new GameRuntimeStatus
        {
            MinecraftVersion = minecraftVersion,
            ForgeVersion = forgeVersion,
            CompatibleJava = compatibleSystemJava ?? compatibleManagedJava,
            MinecraftInstalled = minecraftInstalled,
            ForgeInstalled = forgeInstalled
        };

        status.JavaRuntimes.AddRange(javaRuntimes);
        if (customJava != null && status.JavaRuntimes.All(runtime => !string.Equals(runtime.Path, customJava.Path, System.StringComparison.OrdinalIgnoreCase)))
        {
            status.JavaRuntimes.Insert(0, customJava);
        }

        if (compatibleManagedJava != null)
        {
            status.JavaRuntimes.Add(compatibleManagedJava);
        }

        return status;
    }

    private static async Task<JavaRuntimeInfo?> GetCompatibleCustomJavaAsync()
    {
        if (!string.Equals(LauncherSettings.LoadJavaMode(), "Custom", System.StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var customPath = LauncherSettings.LoadCustomJavaPath();
        if (string.IsNullOrWhiteSpace(customPath) || !System.IO.File.Exists(customPath))
        {
            return null;
        }

        var runtime = await JavaDetectionService.InspectJavaAsync(customPath);
        return runtime is { IsCompatible: true } ? runtime : null;
    }
}
