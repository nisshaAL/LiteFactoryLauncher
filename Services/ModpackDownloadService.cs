using LiteFactoryLauncher.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace LiteFactoryLauncher.Services;

public sealed class ModpackDownloadService
{
    private static readonly HttpClient HttpClient = new();

    public async Task<DownloadResult> DownloadArchiveAsync(
        ModpackManifest manifest,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifest.DownloadUrl))
        {
            return DownloadResult.Error("Manifest does not contain a download URL.");
        }

        if (string.IsNullOrWhiteSpace(manifest.ArchiveFile))
        {
            return DownloadResult.Error("Manifest does not contain an archive file name.");
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "LiteFactoryLauncher");
        var archivePath = Path.Combine(tempDirectory, manifest.ArchiveFile);

        try
        {
            Directory.CreateDirectory(tempDirectory);

            using var request = new HttpRequestMessage(HttpMethod.Get, manifest.DownloadUrl);
            using var response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                archivePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                totalRead += read;

                if (contentLength.HasValue && contentLength.Value > 0)
                {
                    progress?.Report(totalRead * 100d / contentLength.Value);
                }
            }

            progress?.Report(100);
            return DownloadResult.Ok(archivePath);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            TryDeleteFile(archivePath);
            return DownloadResult.Error($"Download failed: {ex.Message}");
        }
    }

    public async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    public static void TryDeleteFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch
        {
            // Temporary cleanup should not hide the real install/download error.
        }
    }
}
