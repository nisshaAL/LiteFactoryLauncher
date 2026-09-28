using LiteFactoryLauncher.Models;

namespace LiteFactoryLauncher.Services;

public sealed class JavaInstallResult
{
    private JavaInstallResult(bool success, JavaRuntimeInfo? runtime, string? errorMessage)
    {
        Success = success;
        Runtime = runtime;
        ErrorMessage = errorMessage;
    }

    public bool Success { get; }

    public JavaRuntimeInfo? Runtime { get; }

    public string? ErrorMessage { get; }

    public static JavaInstallResult Ok(JavaRuntimeInfo runtime)
    {
        return new JavaInstallResult(true, runtime, null);
    }

    public static JavaInstallResult Error(string message)
    {
        return new JavaInstallResult(false, null, message);
    }
}
