namespace LiteFactoryLauncher.Services;

public enum GameProcessStartStatus
{
    Started,
    ValidationFailed,
    AuthenticationRequired,
    AlreadyRunning,
    StartFailed
}
