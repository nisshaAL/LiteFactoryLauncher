namespace LiteFactoryLauncher.Services;

public sealed class DownloadResult
{
    private DownloadResult(bool success, string? archivePath, string? errorMessage)
    {
        Success = success;
        ArchivePath = archivePath;
        ErrorMessage = errorMessage;
    }

    public bool Success { get; }

    public string? ArchivePath { get; }

    public string? ErrorMessage { get; }

    public static DownloadResult Ok(string archivePath)
    {
        return new DownloadResult(true, archivePath, null);
    }

    public static DownloadResult Error(string message)
    {
        return new DownloadResult(false, null, message);
    }
}
