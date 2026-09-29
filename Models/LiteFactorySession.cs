using System;

namespace LiteFactoryLauncher.Models;

public sealed class LiteFactorySession
{
    public string AccessToken { get; set; } = "";

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public LiteFactoryAccount Account { get; set; } = new();
}
