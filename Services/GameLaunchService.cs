using LiteFactoryLauncher.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LiteFactoryLauncher.Services;

public sealed class GameLaunchService
{
    private static readonly string MinecraftRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiteFactory",
        "runtime",
        "minecraft");

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiteFactory",
        "logs",
        "game-launch.log");

    private readonly GameRuntimeService _runtimeService = new();

    public async Task<GameLaunchBuildResult> BuildLaunchPlanAsync(
        GameProfile profile,
        GameLaunchOptions? options = null)
    {
        options ??= new GameLaunchOptions();

        var plan = new GameLaunchPlan
        {
            MinecraftVersion = profile.MinecraftVersion,
            ForgeVersion = profile.ForgeVersion,
            GameDirectory = LauncherSettings.LoadInstallDirectory(),
            WorkingDirectory = LauncherSettings.LoadInstallDirectory(),
            NativesDirectory = Path.Combine(MinecraftRoot, "versions", profile.MinecraftVersion, "natives"),
            AssetsRoot = Path.Combine(MinecraftRoot, "assets")
        };

        try
        {
            Log($"Launch plan build started for profile {profile.Id} ({profile.DisplayName}).");

            var runtimeStatus = await _runtimeService.GetRuntimeStatusAsync(profile.MinecraftVersion, profile.ForgeVersion);
            if (runtimeStatus.CompatibleJava == null || string.IsNullOrWhiteSpace(runtimeStatus.CompatibleJava.Path))
            {
                plan.ValidationErrors.Add("Compatible Java 8 was not found.");
            }
            else
            {
                plan.JavaExecutable = runtimeStatus.CompatibleJava.Path;
                if (!File.Exists(plan.JavaExecutable))
                {
                    plan.ValidationErrors.Add($"Selected Java executable does not exist: {plan.JavaExecutable}");
                }

                if (!runtimeStatus.CompatibleJava.IsCompatible)
                {
                    plan.ValidationErrors.Add("Selected Java runtime is not compatible Java 8.");
                }
            }

            var vanillaProfilePath = GetVersionJsonPath(profile.MinecraftVersion);
            if (!File.Exists(vanillaProfilePath))
            {
                plan.ValidationErrors.Add($"Vanilla Minecraft profile is missing: {vanillaProfilePath}");
                FinalizeValidation(plan);
                LogPlan(plan, profile, vanillaProfilePath, "");
                return GameLaunchBuildResult.FromPlan(plan);
            }

            var forgeProfilePath = FindForgeProfilePath(profile.MinecraftVersion, profile.ForgeVersion);
            if (forgeProfilePath == null)
            {
                plan.ValidationErrors.Add($"Forge profile for {profile.MinecraftVersion} / {profile.ForgeVersion} was not found.");
                FinalizeValidation(plan);
                LogPlan(plan, profile, vanillaProfilePath, "");
                return GameLaunchBuildResult.FromPlan(plan);
            }

            using var vanillaDocument = JsonDocument.Parse(await File.ReadAllTextAsync(vanillaProfilePath));
            using var forgeDocument = JsonDocument.Parse(await File.ReadAllTextAsync(forgeProfilePath));
            var vanilla = vanillaDocument.RootElement;
            var forge = forgeDocument.RootElement;
            var merged = MergeProfiles(vanilla, forge);

            plan.ForgeProfileId = merged.Id;
            plan.MainClass = merged.MainClass;
            plan.AssetsIndexName = merged.AssetsIndexName;
            plan.VersionType = merged.VersionType;

            if (string.IsNullOrWhiteSpace(plan.MainClass))
            {
                plan.ValidationErrors.Add("Launch metadata does not contain a main class.");
            }

            AddJvmArguments(plan, options);
            AddClasspath(plan, merged);
            AddGameArguments(plan, merged, options.Authentication);
            ValidateRequiredPaths(plan, vanillaProfilePath, forgeProfilePath);
            FinalizeValidation(plan);
            LogPlan(plan, profile, vanillaProfilePath, forgeProfilePath);
            return GameLaunchBuildResult.FromPlan(plan);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        {
            plan.ValidationErrors.Add(ex.Message);
            FinalizeValidation(plan);
            Log($"Launch plan build failed: {ex}");
            return GameLaunchBuildResult.Failed($"Launch plan build failed: {ex.Message}", plan);
        }
    }

    private static void AddJvmArguments(GameLaunchPlan plan, GameLaunchOptions options)
    {
        plan.JvmArguments.Add($"-Xms{Math.Clamp(options.MinimumRamMb, 512, 32768)}M");
        plan.JvmArguments.Add($"-Xmx{Math.Clamp(options.MaximumRamMb, 1024, 32768)}M");
        plan.JvmArguments.Add($"-Djava.library.path={plan.NativesDirectory}");
        plan.JvmArguments.Add("-cp");
        plan.JvmArguments.Add(plan.Classpath);
    }

    private static void AddClasspath(GameLaunchPlan plan, MergedProfile metadata)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in metadata.Libraries)
        {
            if (!IsLibraryAllowed(library))
            {
                continue;
            }

            var libraryPath = GetLibraryPath(library);
            if (string.IsNullOrWhiteSpace(libraryPath))
            {
                continue;
            }

            if (seen.Add(libraryPath))
            {
                plan.ClasspathEntries.Add(libraryPath);
            }
        }

        var clientJar = Path.Combine(MinecraftRoot, "versions", plan.MinecraftVersion, $"{plan.MinecraftVersion}.jar");
        if (seen.Add(clientJar))
        {
            plan.ClasspathEntries.Add(clientJar);
        }

        plan.Classpath = string.Join(Path.PathSeparator, plan.ClasspathEntries);
        var classpathIndex = plan.JvmArguments.IndexOf("-cp");
        if (classpathIndex >= 0 && classpathIndex + 1 < plan.JvmArguments.Count)
        {
            plan.JvmArguments[classpathIndex + 1] = plan.Classpath;
        }
    }

    private static void AddGameArguments(GameLaunchPlan plan, MergedProfile metadata, MinecraftAuthContext? authentication)
    {
        var tokens = TokenizeArguments(metadata.MinecraftArguments);
        foreach (var token in tokens)
        {
            plan.GameArguments.Add(ResolveArgumentToken(token, plan, authentication));
        }
    }

    private static string ResolveArgumentToken(string token, GameLaunchPlan plan, MinecraftAuthContext? authentication)
    {
        return token switch
        {
            "${version_name}" => plan.ForgeProfileId,
            "${game_directory}" => plan.GameDirectory,
            "${assets_root}" => plan.AssetsRoot,
            "${assets_index_name}" => plan.AssetsIndexName,
            "${version_type}" => string.IsNullOrWhiteSpace(plan.VersionType) ? "release" : plan.VersionType,
            "${user_type}" => authentication?.UserType ?? "msa",
            "${user_properties}" => authentication?.UserProperties ?? "{}",
            "${auth_player_name}" => ResolveAuthValue(plan, authentication?.PlayerName, "auth_player_name"),
            "${auth_uuid}" => ResolveAuthValue(plan, authentication?.Uuid, "auth_uuid"),
            "${auth_access_token}" => ResolveAuthValue(plan, authentication?.AccessToken, "auth_access_token"),
            _ => token
        };
    }

    private static string ResolveAuthValue(GameLaunchPlan plan, string? value, string field)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (!plan.MissingAuthenticationFields.Contains(field, StringComparer.OrdinalIgnoreCase))
        {
            plan.MissingAuthenticationFields.Add(field);
        }

        return $"<{field}:authentication-required>";
    }

    private static void ValidateRequiredPaths(GameLaunchPlan plan, string vanillaProfilePath, string forgeProfilePath)
    {
        if (!File.Exists(vanillaProfilePath))
        {
            plan.ValidationErrors.Add($"Vanilla profile does not exist: {vanillaProfilePath}");
        }

        if (!File.Exists(forgeProfilePath))
        {
            plan.ValidationErrors.Add($"Forge profile does not exist: {forgeProfilePath}");
        }

        var clientJar = Path.Combine(MinecraftRoot, "versions", plan.MinecraftVersion, $"{plan.MinecraftVersion}.jar");
        if (!File.Exists(clientJar))
        {
            plan.ValidationErrors.Add($"Minecraft client JAR does not exist: {clientJar}");
        }

        foreach (var entry in plan.ClasspathEntries)
        {
            if (!File.Exists(entry))
            {
                plan.ValidationErrors.Add($"Classpath entry is missing: {entry}");
            }
        }

        if (!Directory.Exists(plan.NativesDirectory))
        {
            plan.ValidationErrors.Add($"Natives directory does not exist: {plan.NativesDirectory}");
        }

        var assetIndexPath = Path.Combine(plan.AssetsRoot, "indexes", $"{plan.AssetsIndexName}.json");
        if (string.IsNullOrWhiteSpace(plan.AssetsIndexName) || !File.Exists(assetIndexPath))
        {
            plan.ValidationErrors.Add($"Asset index does not exist: {assetIndexPath}");
        }

        if (!Directory.Exists(plan.GameDirectory))
        {
            plan.ValidationErrors.Add($"Light Factory game directory does not exist: {plan.GameDirectory}");
        }

        foreach (var argument in plan.GameArguments.Concat(plan.JvmArguments))
        {
            if (argument.Contains("${", StringComparison.Ordinal))
            {
                plan.ValidationErrors.Add($"Launch argument contains unresolved token: {argument}");
            }
        }

        if (!string.IsNullOrWhiteSpace(plan.ForgeVersion) &&
            !ContainsArgumentPair(plan.GameArguments, "--tweakClass", "net.minecraftforge.fml.common.launcher.FMLTweaker"))
        {
            plan.ValidationErrors.Add("Forge launch arguments do not contain the required FMLTweaker tweakClass.");
        }
    }

    private static void FinalizeValidation(GameLaunchPlan plan)
    {
        if (plan.ValidationErrors.Count > 0)
        {
            plan.ValidationState = GameLaunchValidationState.RuntimeInvalid;
            return;
        }

        plan.ValidationState = plan.MissingAuthenticationFields.Count > 0
            ? GameLaunchValidationState.AuthenticationRequired
            : GameLaunchValidationState.Ready;
    }

    private static MergedProfile MergeProfiles(JsonElement vanilla, JsonElement forge)
    {
        var id = GetString(forge, "id");
        var mainClass = GetString(forge, "mainClass");
        if (string.IsNullOrWhiteSpace(mainClass))
        {
            mainClass = GetString(vanilla, "mainClass");
        }

        var minecraftArguments = GetString(forge, "minecraftArguments");
        if (string.IsNullOrWhiteSpace(minecraftArguments))
        {
            minecraftArguments = GetString(vanilla, "minecraftArguments");
        }

        var assetsIndexName = GetAssetIndexName(forge);
        if (string.IsNullOrWhiteSpace(assetsIndexName))
        {
            assetsIndexName = GetAssetIndexName(vanilla);
        }

        var versionType = GetString(forge, "type");
        if (string.IsNullOrWhiteSpace(versionType))
        {
            versionType = GetString(vanilla, "type");
        }

        var libraries = new List<JsonElement>();
        AddLibraries(vanilla, libraries);
        AddLibraries(forge, libraries);

        return new MergedProfile(id, mainClass, minecraftArguments, assetsIndexName, versionType, libraries);
    }

    private static void AddLibraries(JsonElement profile, List<JsonElement> libraries)
    {
        if (!profile.TryGetProperty("libraries", out var librariesElement) ||
            librariesElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var library in librariesElement.EnumerateArray())
        {
            libraries.Add(library.Clone());
        }
    }

    private static string GetAssetIndexName(JsonElement profile)
    {
        if (profile.TryGetProperty("assetIndex", out var assetIndex) &&
            assetIndex.TryGetProperty("id", out var idElement))
        {
            return idElement.GetString() ?? "";
        }

        return GetString(profile, "assets");
    }

    private static string? FindForgeProfilePath(string minecraftVersion, string forgeVersion)
    {
        var versionsDirectory = Path.Combine(MinecraftRoot, "versions");
        if (!Directory.Exists(versionsDirectory))
        {
            return null;
        }

        foreach (var profilePath in Directory.EnumerateFiles(versionsDirectory, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(profilePath));
                var root = document.RootElement;
                var id = GetString(root, "id");
                if (id.Contains("forge", StringComparison.OrdinalIgnoreCase) &&
                    id.Contains(forgeVersion, StringComparison.OrdinalIgnoreCase) &&
                    (id.Contains(minecraftVersion, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(GetString(root, "inheritsFrom"), minecraftVersion, StringComparison.OrdinalIgnoreCase)))
                {
                    return profilePath;
                }
            }
            catch (Exception ex)
            {
                Log($"Skipping invalid Forge profile candidate {profilePath}: {ex}");
            }
        }

        return null;
    }

    private static string? GetLibraryPath(JsonElement library)
    {
        if (library.TryGetProperty("downloads", out var downloads))
        {
            if (downloads.TryGetProperty("artifact", out var artifact) &&
                artifact.TryGetProperty("path", out var pathElement) &&
                !string.IsNullOrWhiteSpace(pathElement.GetString()))
            {
                return Path.Combine(MinecraftRoot, "libraries", pathElement.GetString()!.Replace('/', Path.DirectorySeparatorChar));
            }

            return null;
        }

        var name = library.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        return string.IsNullOrWhiteSpace(name)
            ? null
            : Path.Combine(MinecraftRoot, "libraries", GetMavenPath(name));
    }

    private static string GetMavenPath(string name)
    {
        var extension = "jar";
        var baseName = name;
        var extensionSeparator = name.IndexOf('@');
        if (extensionSeparator >= 0)
        {
            baseName = name[..extensionSeparator];
            extension = name[(extensionSeparator + 1)..];
        }

        var parts = baseName.Split(':');
        if (parts.Length < 3)
        {
            return baseName.Replace(':', Path.DirectorySeparatorChar);
        }

        var groupPath = parts[0].Replace('.', Path.DirectorySeparatorChar);
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? $"-{parts[3]}" : "";
        return Path.Combine(groupPath, artifact, version, $"{artifact}-{version}{classifier}.{extension}");
    }

    private static bool IsLibraryAllowed(JsonElement library)
    {
        if (!library.TryGetProperty("rules", out var rules))
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            if (!RuleAppliesToWindows(rule))
            {
                continue;
            }

            allowed = string.Equals(rule.GetProperty("action").GetString(), "allow", StringComparison.OrdinalIgnoreCase);
        }

        return allowed;
    }

    private static bool RuleAppliesToWindows(JsonElement rule)
    {
        if (!rule.TryGetProperty("os", out var os))
        {
            return true;
        }

        return os.TryGetProperty("name", out var name) &&
               string.Equals(name.GetString(), "windows", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> TokenizeArguments(string arguments)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return tokens;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var character in arguments)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                AddToken(tokens, current);
                continue;
            }

            current.Append(character);
        }

        AddToken(tokens, current);
        return tokens;
    }

    private static void AddToken(List<string> tokens, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        tokens.Add(current.ToString());
        current.Clear();
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) ? value.GetString() ?? "" : "";
    }

    private static string GetVersionJsonPath(string version)
    {
        return Path.Combine(MinecraftRoot, "versions", version, $"{version}.json");
    }

    private static void LogPlan(GameLaunchPlan plan, GameProfile profile, string vanillaProfilePath, string forgeProfilePath)
    {
        var log = new StringBuilder()
            .AppendLine($"Launch plan for profile {profile.Id}")
            .AppendLine($"Validation: {plan.ValidationState}")
            .AppendLine($"Selected Java: {plan.JavaExecutable}")
            .AppendLine($"Vanilla metadata: {vanillaProfilePath}")
            .AppendLine($"Forge metadata: {forgeProfilePath}")
            .AppendLine($"Forge profile id: {plan.ForgeProfileId}")
            .AppendLine($"Main class: {plan.MainClass}")
            .AppendLine($"Game directory: {plan.GameDirectory}")
            .AppendLine($"Natives directory: {plan.NativesDirectory}")
            .AppendLine($"Assets root: {plan.AssetsRoot}")
            .AppendLine($"Assets index: {plan.AssetsIndexName}")
            .AppendLine($"Version type: {plan.VersionType}")
            .AppendLine($"Classpath entries: {plan.ClasspathEntries.Count}")
            .AppendLine($"JVM memory arguments: {string.Join(" ", GetMemoryArguments(plan.JvmArguments))}")
            .AppendLine($"Game arguments count: {plan.GameArguments.Count}")
            .AppendLine($"Forge tweakClass present: {ContainsArgumentPair(plan.GameArguments, "--tweakClass", "net.minecraftforge.fml.common.launcher.FMLTweaker")}")
            .AppendLine($"Authentication arguments present: {ContainsArgument(plan.GameArguments, "--username") && ContainsArgument(plan.GameArguments, "--uuid") && ContainsArgument(plan.GameArguments, "--accessToken")}");

        if (plan.ValidationErrors.Count > 0)
        {
            log.AppendLine($"Validation errors: {string.Join(" | ", plan.ValidationErrors)}");
        }

        if (plan.MissingAuthenticationFields.Count > 0)
        {
            log.AppendLine($"Missing authentication fields: {string.Join(", ", plan.MissingAuthenticationFields)}");
        }

        Log(log.ToString());
    }

    private static IEnumerable<string> GetMemoryArguments(IReadOnlyList<string> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.StartsWith("-Xms", StringComparison.OrdinalIgnoreCase) ||
                argument.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase))
            {
                yield return argument;
            }
        }
    }

    private static bool ContainsArgument(IReadOnlyList<string> arguments, string name)
    {
        return arguments.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsArgumentPair(IReadOnlyList<string> arguments, string name, string value)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(arguments[index + 1], value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void Log(string message)
    {
        Console.Error.WriteLine(message);
        Debug.WriteLine(message);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{message}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to write launch log: {ex}");
            Debug.WriteLine($"Failed to write launch log: {ex}");
        }
    }

    private sealed record MergedProfile(
        string Id,
        string MainClass,
        string MinecraftArguments,
        string AssetsIndexName,
        string VersionType,
        IReadOnlyList<JsonElement> Libraries);
}
