using System.Diagnostics;
using System.Runtime.Versioning;
using Framework.BuildingBlocks.Helpers;

namespace Framework.BuildingBlocks.Extensions.System;

public static partial class ProcessExtensions
{
    [SupportedOSPlatform("windows")]
    public static IEnumerable<int> GetAncestorProcessIds(this Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Only supported on Windows");
        }

        return getAncestorProcessIdsIterator();

        IEnumerable<int> getAncestorProcessIdsIterator()
        {
            var returnedProcesses = new HashSet<int>();

            var processId = process.Id;
            var processes = ProcessHelper.GetProcesses().ToList();
            var found = true;

            while (found)
            {
                found = false;

                foreach (var entry in processes)
                {
                    if (entry.ProcessId == processId && returnedProcesses.Add(entry.ParentProcessId))
                    {
                        yield return entry.ParentProcessId;

                        processId = entry.ParentProcessId;
                        found = true;
                    }
                }

                if (!found)
                {
                    yield break;
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    public static IEnumerable<Process> GetAncestorProcesses(this Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        return getAncestorProcesses();

        IEnumerable<Process> getAncestorProcesses()
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Only supported on Windows");
            }

            foreach (var entry in getAncestorProcessIdsIterator())
            {
                Process? p = null;

                try
                {
                    p = entry.ToProcess();

                    if (p.StartTime > process.StartTime)
                    {
                        continue;
                    }
                }
                catch (ArgumentException)
                {
                    // process might have exited since the snapshot, ignore it
                }

                if (p is not null)
                {
                    yield return p;
                }
            }

            IEnumerable<ProcessEntry> getAncestorProcessIdsIterator()
            {
                var returnedProcesses = new HashSet<int>();
                var processId = process.Id;
                var processes = ProcessHelper.GetProcesses().ToList();
                var found = true;

                while (found)
                {
                    found = false;

                    foreach (var entry in processes)
                    {
                        if (entry.ProcessId == processId && returnedProcesses.Add(entry.ParentProcessId))
                        {
                            yield return entry;

                            processId = entry.ParentProcessId;
                            found = true;
                        }
                    }

                    if (!found)
                    {
                        yield break;
                    }
                }
            }
        }
    }
}
