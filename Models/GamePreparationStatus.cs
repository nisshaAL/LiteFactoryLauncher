namespace LiteFactoryLauncher.Models;

public sealed class GamePreparationStatus
{
    public GamePreparationPhase Phase { get; set; } = GamePreparationPhase.CheckingEnvironment;

    public string StatusText { get; set; } = "";

    public double OverallProgress { get; set; }

    public double ComponentProgress { get; set; }

    public string JavaStatus { get; set; } = "Waiting...";

    public string MinecraftStatus { get; set; } = "Waiting...";

    public string ForgeStatus { get; set; } = "Waiting...";

    public string ModpackStatus { get; set; } = "Waiting...";

    public string? ErrorMessage { get; set; }
}
