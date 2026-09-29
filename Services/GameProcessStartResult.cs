namespace LiteFactoryLauncher.Services;

public sealed class GameProcessStartResult
{
    private GameProcessStartResult(bool success, GameProcessStartStatus status, string message, int? processId)
    {
        Success = success;
        Status = status;
        Message = message;
        ProcessId = processId;
    }

    public bool Success { get; }

    public GameProcessStartStatus Status { get; }

    public string Message { get; }

    public int? ProcessId { get; }

    public static GameProcessStartResult Started(int processId)
    {
        return new GameProcessStartResult(true, GameProcessStartStatus.Started, "Game process started.", processId);
    }

    public static GameProcessStartResult Error(GameProcessStartStatus status, string message)
    {
        return new GameProcessStartResult(false, status, message, null);
    }
}
