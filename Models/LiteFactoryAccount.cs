using System;

namespace LiteFactoryLauncher.Models;

public sealed class LiteFactoryAccount
{
    public Guid Id { get; set; }

    public string Email { get; set; } = "";

    public string Nickname { get; set; } = "";

    public DateTimeOffset CreatedAtUtc { get; set; }
}
