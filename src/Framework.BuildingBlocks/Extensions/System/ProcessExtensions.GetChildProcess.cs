using System.Diagnostics;
using System.Runtime.Versioning;
using Framework.BuildingBlocks.Helpers;

namespace Framework.BuildingBlocks.Extensions.System;

public static partial class ProcessExtensions
{
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<Process> GetChildProcesses(this Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Only supported on Windows");
        }

        var children = new List<Process>();
        _GetChildProcesses(process, children, 1, 0);

        return children;
    }

    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<Process> GetDescendantProcesses(this Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Only supported on Windows");
        }

        var children = new List<Process>();
        _GetChildProcesses(process, children, int.MaxValue, 0);

        return children;
    }

    [SupportedOSPlatform("windows")]
    private static void _GetChildProcesses(Process process, List<Process> children, int maxDepth, int currentDepth)
    {
        ArgumentNullException.ThrowIfNull(process);

        var entries = new List<ProcessEntry>(100);

        entries.AddRange(ProcessHelper.GetProcesses());
        _GetChildProcesses(entries, process, children, maxDepth, currentDepth);
    }

    private static void _GetChildProcesses(
        List<ProcessEntry> entries,
        Process process,
        List<Process> children,
        int maxDepth,
        int currentDepth
    )
    {
        var processId = process.Id;

        foreach (var entry in entries)
        {
            if (entry.ParentProcessId != processId)
            {
                continue;
            }

            try
            {
                var child = entry.ToProcess();

                if (child.StartTime < process.StartTime)
                {
                    continue;
                }

                children.Add(child);

                if (currentDepth < maxDepth)
                {
                    _GetChildProcesses(entries, child, children, maxDepth, currentDepth + 1);
                }
            }
            catch (ArgumentException)
            {
                // process might have exited since the snapshot, ignore it
            }
        }
    }
}
