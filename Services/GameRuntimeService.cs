using LiteFactoryLauncher.Models;
using System.Linq;
using System.Threading.Tasks;

namespace LiteFactoryLauncher.Services;

public sealed class GameRuntimeService
{
    private readonly JavaDetectionService _javaDetectionService = new();
    private readonly ManagedJavaService _managedJavaService = new();

    public async Task<GameRuntimeStatus> GetRuntimeStatusAsync(string minecraftVersion, string forgeVersion)
    {
        var javaRuntimes = await _javaDetectionService.DetectJavaRuntimesAsync();
        var compatibleSystemJava = javaRuntimes.FirstOrDefault(runtime => runtime.IsCompatible);
        var compatibleManagedJava = compatibleSystemJava == null
            ? await _managedJavaService.GetInstalledRuntimeAsync()
            : null;

        var status = new GameRuntimeStatus
        {
            MinecraftVersion = minecraftVersion,
            ForgeVersion = forgeVersion,
            CompatibleJava = compatibleSystemJava ?? compatibleManagedJava,
            MinecraftInstalled = false,
            ForgeInstalled = false
        };

        status.JavaRuntimes.AddRange(javaRuntimes);
        if (compatibleManagedJava != null)
        {
            status.JavaRuntimes.Add(compatibleManagedJava);
        }

        return status;
    }
}
