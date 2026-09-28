namespace LiteFactoryLauncher.Services;

public sealed class InstallResult
{
    private InstallResult(bool success, string? errorMessage)
    {
        Success = success;
        ErrorMessage = errorMessage;
    }

    public bool Success { get; }

    public string? ErrorMessage { get; }

    public static InstallResult Ok()
    {
        return new InstallResult(true, null);
    }

    public static InstallResult Error(string message)
    {
        return new InstallResult(false, message);
    }
}
