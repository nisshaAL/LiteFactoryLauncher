using System;

namespace LiteFactoryLauncher.Services;

public sealed class GameProcessExitedEventArgs : EventArgs
{
    public GameProcessExitedEventArgs(int processId, int exitCode)
    {
        ProcessId = processId;
        ExitCode = exitCode;
    }

    public int ProcessId { get; }

    public int ExitCode { get; }
}
