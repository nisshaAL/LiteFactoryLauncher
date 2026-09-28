namespace LiteFactoryLauncher.Models;

public sealed class JavaRuntimeInfo
{
    public string Path { get; set; } = "";

    public string VersionText { get; set; } = "";

    public string Version { get; set; } = "";

    public int? MajorVersion { get; set; }

    public bool? Is64Bit { get; set; }

    public bool IsCompatible { get; set; }

    public string? Error { get; set; }
}
