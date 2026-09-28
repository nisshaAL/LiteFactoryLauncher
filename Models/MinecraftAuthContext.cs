namespace LiteFactoryLauncher.Models;

public sealed class MinecraftAuthContext
{
    public string PlayerName { get; set; } = "";

    public string Uuid { get; set; } = "";

    public string AccessToken { get; set; } = "";

    public string UserType { get; set; } = "msa";

    public string UserProperties { get; set; } = "{}";

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(PlayerName) &&
        !string.IsNullOrWhiteSpace(Uuid) &&
        !string.IsNullOrWhiteSpace(AccessToken);
}
