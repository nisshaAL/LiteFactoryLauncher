using LiteFactoryLauncher.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LiteFactoryLauncher.Services;

public sealed class JavaDetectionService
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<JavaRuntimeInfo>> DetectJavaRuntimesAsync()
    {
        var candidates = FindJavaCandidates();
        var runtimes = new List<JavaRuntimeInfo>();

        foreach (var candidate in candidates)
        {
            var runtime = await InspectJavaAsync(candidate);
            if (runtime != null)
            {
                runtimes.Add(runtime);
            }
        }

        return runtimes
            .OrderByDescending(runtime => runtime.IsCompatible)
            .ThenByDescending(runtime => runtime.Is64Bit == true)
            .ThenBy(runtime => runtime.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> FindJavaCandidates()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddJavaHomeCandidate(candidates);
        AddPathCandidates(candidates);
        AddCommonInstallCandidates(candidates);

        return candidates.Where(File.Exists);
    }

    private static void AddJavaHomeCandidate(HashSet<string> candidates)
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            candidates.Add(Path.Combine(javaHome, "bin", "java.exe"));
        }
    }

    private static void AddPathCandidates(HashSet<string> candidates)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            candidates.Add(Path.Combine(directory, "java.exe"));
        }
    }

    private static void AddCommonInstallCandidates(HashSet<string> candidates)
    {
        var baseDirectories = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Foundation"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Amazon Corretto"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Java")
        };

        foreach (var baseDirectory in baseDirectories.Where(Directory.Exists))
        {
            foreach (var installDirectory in Directory.EnumerateDirectories(baseDirectory))
            {
                candidates.Add(Path.Combine(installDirectory, "bin", "java.exe"));
            }
        }
    }

    private static async Task<JavaRuntimeInfo?> InspectJavaAsync(string javaPath)
    {
        if (!File.Exists(javaPath))
        {
            return null;
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            using var timeout = new CancellationTokenSource(VersionTimeout);
            await process.WaitForExitAsync(timeout.Token);

            var output = $"{await outputTask}{await errorTask}";
            var version = ParseVersion(output);
            var majorVersion = ParseMajorVersion(version);

            return new JavaRuntimeInfo
            {
                Path = javaPath,
                VersionText = output.Trim(),
                Version = version,
                MajorVersion = majorVersion,
                Is64Bit = output.Contains("64-Bit", StringComparison.OrdinalIgnoreCase) ||
                          output.Contains("64 bit", StringComparison.OrdinalIgnoreCase),
                IsCompatible = majorVersion == 8
            };
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new JavaRuntimeInfo
            {
                Path = javaPath,
                Error = "java.exe -version timed out."
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new JavaRuntimeInfo
            {
                Path = javaPath,
                Error = ex.Message
            };
        }
    }

    private static string ParseVersion(string output)
    {
        var match = Regex.Match(output, "\"(?<version>[^\"]+)\"");
        return match.Success ? match.Groups["version"].Value : "";
    }

    private static int? ParseMajorVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        if (parts[0] == "1" && parts.Length > 1 && int.TryParse(parts[1], out var legacyMajor))
        {
            return legacyMajor;
        }

        return int.TryParse(parts[0], out var major) ? major : null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Runtime probing should never crash the launcher.
        }
    }
}
