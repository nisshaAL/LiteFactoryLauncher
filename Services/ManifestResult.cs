using LiteFactoryLauncher.Models;

namespace LiteFactoryLauncher.Services;

public sealed class ManifestResult
{
    private ManifestResult(bool success, ModpackManifest? manifest, string? errorMessage)
    {
        Success = success;
        Manifest = manifest;
        ErrorMessage = errorMessage;
    }

    public bool Success { get; }

    public ModpackManifest? Manifest { get; }

    public string? ErrorMessage { get; }

    public static ManifestResult Ok(ModpackManifest manifest)
    {
        return new ManifestResult(true, manifest, null);
    }

    public static ManifestResult Error(string message)
    {
        return new ManifestResult(false, null, message);
    }
}
