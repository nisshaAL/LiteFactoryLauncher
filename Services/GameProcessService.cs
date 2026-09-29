using LiteFactoryLauncher.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LiteFactoryLauncher.Services;

public sealed class GameProcessService
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiteFactory",
        "logs",
        "game-process.log");

    private readonly object _lock = new();
    private Process? _runningProcess;

    public event EventHandler<GameProcessExitedEventArgs>? ProcessExited;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _runningProcess is { HasExited: false };
            }
        }
    }

    public GameProcessStartResult Start(GameLaunchPlan? plan)
    {
        lock (_lock)
        {
            if (_runningProcess is { HasExited: false })
            {
                return GameProcessStartResult.Error(GameProcessStartStatus.AlreadyRunning, "Minecraft is already running.");
            }
        }

        var validationError = ValidateExecutablePlan(plan);
        if (validationError != null)
        {
            Log($"Game process start refused: {validationError.Status} - {validationError.Message}");
            return validationError;
        }

        try
        {
            var process = CreateProcess(plan!);
            LogStart(plan!);

            if (!process.Start())
            {
                return GameProcessStartResult.Error(GameProcessStartStatus.StartFailed, "Could not start Java process.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            lock (_lock)
            {
                _runningProcess = process;
            }

            Log($"Game process started. ProcessId={process.Id}");
            return GameProcessStartResult.Started(process.Id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Log($"Game process start failed:{Environment.NewLine}{ex}");
            return GameProcessStartResult.Error(GameProcessStartStatus.StartFailed, $"Could not start Minecraft: {ex.Message}");
        }
    }

    private static GameProcessStartResult? ValidateExecutablePlan(GameLaunchPlan? plan)
    {
        if (plan == null)
        {
            return GameProcessStartResult.Error(GameProcessStartStatus.ValidationFailed, "Launch plan was not built.");
        }

        if (plan.ValidationState == GameLaunchValidationState.AuthenticationRequired)
        {
            return GameProcessStartResult.Error(
                GameProcessStartStatus.AuthenticationRequired,
                "Game runtime is ready. Minecraft authentication is required before launch.");
        }

        if (plan.ValidationState != GameLaunchValidationState.Ready)
        {
            var message = plan.ValidationErrors.Count > 0
                ? string.Join(Environment.NewLine, plan.ValidationErrors)
                : "Launch plan is not executable.";
            return GameProcessStartResult.Error(GameProcessStartStatus.ValidationFailed, message);
        }

        if (string.IsNullOrWhiteSpace(plan.JavaExecutable) || !File.Exists(plan.JavaExecutable))
        {
            return GameProcessStartResult.Error(GameProcessStartStatus.ValidationFailed, "Java executable does not exist.");
        }

        if (string.IsNullOrWhiteSpace(plan.WorkingDirectory) || !Directory.Exists(plan.WorkingDirectory))
        {
            return GameProcessStartResult.Error(GameProcessStartStatus.ValidationFailed, "Working directory does not exist.");
        }

        if (string.IsNullOrWhiteSpace(plan.GameDirectory) || !Directory.Exists(plan.GameDirectory))
        {
            return GameProcessStartResult.Error(GameProcessStartStatus.ValidationFailed, "Game directory does not exist.");
        }

        if (string.IsNullOrWhiteSpace(plan.MainClass))
        {
            return GameProcessStartResult.Error(GameProcessStartStatus.ValidationFailed, "Launch plan does not contain a main class.");
        }

        if (plan.ClasspathEntries.Count == 0 || string.IsNullOrWhiteSpace(plan.Classpath))
        {
            return GameProcessStartResult.Error(GameProcessStartStatus.ValidationFailed, "Launch plan classpath is empty.");
        }

        if (plan.ClasspathEntries.Any(entry => !File.Exists(entry)))
        {
            return GameProcessStartResult.Error(GameProcessStartStatus.ValidationFailed, "Launch plan contains missing classpath entries.");
        }

        if (!string.IsNullOrWhiteSpace(plan.NativesDirectory) && !Directory.Exists(plan.NativesDirectory))
        {
            return GameProcessStartResult.Error(GameProcessStartStatus.ValidationFailed, "Natives directory does not exist.");
        }

        if (plan.JvmArguments.Count == 0 || plan.GameArguments.Count == 0)
        {
            return GameProcessStartResult.Error(GameProcessStartStatus.ValidationFailed, "Launch plan arguments are incomplete.");
        }

        return null;
    }

    private Process CreateProcess(GameLaunchPlan plan)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = plan.JavaExecutable,
                WorkingDirectory = plan.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        foreach (var argument in plan.JvmArguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.ArgumentList.Add(plan.MainClass);

        foreach (var argument in plan.GameArguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.OutputDataReceived += (_, args) => LogProcessOutput("stdout", args.Data);
        process.ErrorDataReceived += (_, args) => LogProcessOutput("stderr", args.Data);
        process.Exited += (_, _) => HandleExited(process);
        return process;
    }

    private void HandleExited(Process process)
    {
        var processId = SafeGetProcessId(process);
        var exitCode = SafeGetExitCode(process);

        lock (_lock)
        {
            if (ReferenceEquals(_runningProcess, process))
            {
                _runningProcess = null;
            }
        }

        Log($"Game process exited. ProcessId={processId}, ExitCode={exitCode}");
        ProcessExited?.Invoke(this, new GameProcessExitedEventArgs(processId, exitCode));
        process.Dispose();
    }

    private static int SafeGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return 0;
        }
    }

    private static int SafeGetExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    private static void LogStart(GameLaunchPlan plan)
    {
        Log(
            $"Game process launch requested.{Environment.NewLine}" +
            $"Java={plan.JavaExecutable}{Environment.NewLine}" +
            $"WorkingDirectory={plan.WorkingDirectory}{Environment.NewLine}" +
            $"GameDirectory={plan.GameDirectory}{Environment.NewLine}" +
            $"MainClass={plan.MainClass}{Environment.NewLine}" +
            $"ClasspathEntries={plan.ClasspathEntries.Count}{Environment.NewLine}" +
            $"JvmArguments={string.Join(" ", SanitizeArguments(plan.JvmArguments))}{Environment.NewLine}" +
            $"GameArguments={string.Join(" ", SanitizeArguments(plan.GameArguments))}");
    }

    private static void LogProcessOutput(string streamName, string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return;
        }

        Log($"Minecraft {streamName}: {SanitizeLine(data)}");
    }

    private static string[] SanitizeArguments(System.Collections.Generic.IReadOnlyList<string> arguments)
    {
        var sanitized = new string[arguments.Count];
        var redactNext = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (redactNext)
            {
                sanitized[index] = "<redacted>";
                redactNext = false;
                continue;
            }

            sanitized[index] = SanitizeLine(argument);
            if (argument.Contains("accessToken", StringComparison.OrdinalIgnoreCase) ||
                argument.Contains("access_token", StringComparison.OrdinalIgnoreCase) ||
                argument.Contains("auth_access_token", StringComparison.OrdinalIgnoreCase))
            {
                redactNext = true;
            }
        }

        return sanitized;
    }

    private static string SanitizeLine(string value)
    {
        return value
            .Replace("${auth_access_token}", "<auth_access_token>", StringComparison.OrdinalIgnoreCase)
            .Replace("auth_access_token", "<auth_access_token>", StringComparison.OrdinalIgnoreCase);
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
            Console.Error.WriteLine($"Failed to write game process log: {ex}");
            Debug.WriteLine($"Failed to write game process log: {ex}");
        }
    }
}
