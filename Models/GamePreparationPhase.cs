namespace LiteFactoryLauncher.Models;

public enum GamePreparationPhase
{
    CheckingEnvironment,
    PreparingJava,
    PreparingMinecraft,
    PreparingForge,
    CheckingModpack,
    DownloadingModpack,
    InstallingModpack,
    UpdatingModpack,
    FinalValidation,
    ReadyToLaunch,
    Failed
}
