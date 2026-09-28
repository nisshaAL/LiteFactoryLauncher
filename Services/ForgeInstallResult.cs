namespace LiteFactoryLauncher.Services;

public sealed class ForgeInstallResult
{
    private ForgeInstallResult(bool success, string? errorMessage, string? diagnosticText)
    {
        Success = success;
        ErrorMessage = errorMessage;
        DiagnosticText = diagnosticText;
    }

    public bool Success { get; }

    public string? ErrorMessage { get; }

    public string? DiagnosticText { get; }

    public static ForgeInstallResult Ok()
    {
        return new ForgeInstallResult(true, null, null);
    }

    public static ForgeInstallResult Error(string message, string? diagnosticText = null)
    {
        return new ForgeInstallResult(false, message, diagnosticText);
    }
}
