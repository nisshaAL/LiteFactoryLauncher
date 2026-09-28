namespace LiteFactoryLauncher.Models;

public sealed class GameLaunchOptions
{
    public int MinimumRamMb { get; set; } = 1024;

    public int MaximumRamMb { get; set; } = 4096;

    public MinecraftAuthContext? Authentication { get; set; }
}
