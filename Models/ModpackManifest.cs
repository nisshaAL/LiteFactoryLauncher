namespace LiteFactoryLauncher.Models;

public sealed class ModpackManifest
{
    public string ProjectName { get; set; } = "";

    public string MinecraftVersion { get; set; } = "";

    public string ForgeVersion { get; set; } = "";

    public string PackVersion { get; set; } = "";

    public string ArchiveFile { get; set; } = "";

    public string DownloadUrl { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public string LaunchTarget { get; set; } = "";

    public long ArchiveSize { get; set; }
}
