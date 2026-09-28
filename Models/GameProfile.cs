namespace LiteFactoryLauncher.Models;

public sealed class GameProfile
{
    public string Id { get; set; } = "light-factory";

    public string DisplayName { get; set; } = "Light Factory";

    public string MinecraftVersion { get; set; } = "1.12.2";

    public string ForgeVersion { get; set; } = "14.23.5.2860";

    public ModpackManifest Manifest { get; set; } = new();
}
