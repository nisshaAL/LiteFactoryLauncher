using System.Collections.Generic;

namespace LiteFactoryLauncher.Models;

public sealed class GameRuntimeStatus
{
    public string MinecraftVersion { get; set; } = "1.12.2";

    public string ForgeVersion { get; set; } = "14.23.5.2860";

    public bool JavaFound => JavaRuntimes.Count > 0;

    public bool CompatibleJavaFound => CompatibleJava != null;

    public bool CanInstallManagedJava => !CompatibleJavaFound;

    public string JavaPath => CompatibleJava?.Path ?? (JavaRuntimes.Count > 0 ? JavaRuntimes[0].Path : "");

    public string JavaVersion => CompatibleJava?.Version ?? (JavaRuntimes.Count > 0 ? JavaRuntimes[0].Version : "");

    public JavaRuntimeInfo? CompatibleJava { get; set; }

    public List<JavaRuntimeInfo> JavaRuntimes { get; } = new();

    public bool MinecraftInstalled { get; set; }

    public bool ForgeInstalled { get; set; }
}
