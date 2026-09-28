using LiteFactoryLauncher.Models;
using System.Linq;
using System.Threading.Tasks;

namespace LiteFactoryLauncher.Services;

public sealed class GameRuntimeService
{
    private readonly JavaDetectionService _javaDetectionService = new();

    public async Task<GameRuntimeStatus> GetRuntimeStatusAsync(string minecraftVersion, string forgeVersion)
    {
        var javaRuntimes = await _javaDetectionService.DetectJavaRuntimesAsync();
        var status = new GameRuntimeStatus
        {
            MinecraftVersion = minecraftVersion,
            ForgeVersion = forgeVersion,
            CompatibleJava = javaRuntimes.FirstOrDefault(runtime => runtime.IsCompatible),
            MinecraftInstalled = false,
            ForgeInstalled = false
        };

        status.JavaRuntimes.AddRange(javaRuntimes);
        return status;
    }
}
