using LiteFactoryLauncher.Models;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LiteFactoryLauncher.Services;

public sealed class RemoteManifestService
{
    public const string ManifestUrl =
        "https://raw.githubusercontent.com/nisshaAL/LiteFactoryLauncher/refs/heads/main/manifest.json";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public async Task<ManifestResult> LoadRemoteManifestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await HttpClient.GetAsync(ManifestUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return ManifestResult.Error("Remote manifest is empty.");
            }

            var manifest = JsonSerializer.Deserialize<ModpackManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (manifest == null)
            {
                return ManifestResult.Error("Remote manifest could not be read.");
            }

            var validationError = ValidateManifest(manifest);
            return validationError == null
                ? ManifestResult.Ok(manifest)
                : ManifestResult.Error(validationError);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return ManifestResult.Error($"Remote manifest check failed: {ex.Message}");
        }
    }

    private static string? ValidateManifest(ModpackManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.PackVersion))
        {
            return "Remote manifest does not contain packVersion.";
        }

        if (string.IsNullOrWhiteSpace(manifest.DownloadUrl))
        {
            return "Remote manifest does not contain downloadUrl.";
        }

        if (string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            return "Remote manifest does not contain sha256.";
        }

        return null;
    }
}
