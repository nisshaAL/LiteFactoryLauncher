namespace LiteFactoryLauncher.Services;

public sealed class MinecraftInstallResult
{
    private MinecraftInstallResult(bool success, string? errorMessage, string? diagnosticText)
    {
        Success = success;
        ErrorMessage = errorMessage;
        DiagnosticText = diagnosticText;
    }

    public bool Success { get; }

    public string? ErrorMessage { get; }

    public string? DiagnosticText { get; }

    public static MinecraftInstallResult Ok()
    {
        return new MinecraftInstallResult(true, null, null);
    }

    public static MinecraftInstallResult Error(string message, string? diagnosticText = null)
    {
        return new MinecraftInstallResult(false, message, diagnosticText);
    }
}
