using System.Collections.Generic;

namespace LiteFactoryLauncher.Models;

public sealed class GameLaunchPlan
{
    public string JavaExecutable { get; set; } = "";

    public string WorkingDirectory { get; set; } = "";

    public string MainClass { get; set; } = "";

    public List<string> JvmArguments { get; } = new();

    public List<string> GameArguments { get; } = new();

    public List<string> ClasspathEntries { get; } = new();

    public string Classpath { get; set; } = "";

    public string NativesDirectory { get; set; } = "";

    public string MinecraftVersion { get; set; } = "";

    public string ForgeVersion { get; set; } = "";

    public string ForgeProfileId { get; set; } = "";

    public string GameDirectory { get; set; } = "";

    public string AssetsRoot { get; set; } = "";

    public string AssetsIndexName { get; set; } = "";

    public string VersionType { get; set; } = "";

    public GameLaunchValidationState ValidationState { get; set; } = GameLaunchValidationState.RuntimeInvalid;

    public List<string> ValidationErrors { get; } = new();

    public List<string> MissingAuthenticationFields { get; } = new();
}
