using LiteFactoryLauncher.Models;

namespace LiteFactoryLauncher.Services;

public sealed class GamePreparationResult
{
    private GamePreparationResult(bool success, string? errorMessage, string? diagnosticText, ModpackManifest? manifest)
    {
        Success = success;
        ErrorMessage = errorMessage;
        DiagnosticText = diagnosticText;
        Manifest = manifest;
    }

    public bool Success { get; }

    public string? ErrorMessage { get; }

    public string? DiagnosticText { get; }

    public ModpackManifest? Manifest { get; }

    public static GamePreparationResult Ready(ModpackManifest manifest)
    {
        return new GamePreparationResult(true, null, null, manifest);
    }

    public static GamePreparationResult Failed(string message, string? diagnosticText = null, ModpackManifest? manifest = null)
    {
        return new GamePreparationResult(false, message, diagnosticText, manifest);
    }
}
