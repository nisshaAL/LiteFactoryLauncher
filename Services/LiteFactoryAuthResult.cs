using LiteFactoryLauncher.Models;

namespace LiteFactoryLauncher.Services;

public sealed class LiteFactoryAuthResult
{
    private LiteFactoryAuthResult(bool success, LiteFactorySession? session, string? errorMessage)
    {
        Success = success;
        Session = session;
        ErrorMessage = errorMessage;
    }

    public bool Success { get; }

    public LiteFactorySession? Session { get; }

    public string? ErrorMessage { get; }

    public static LiteFactoryAuthResult Ok(LiteFactorySession session)
    {
        return new LiteFactoryAuthResult(true, session, null);
    }

    public static LiteFactoryAuthResult Error(string message)
    {
        return new LiteFactoryAuthResult(false, null, message);
    }
}
