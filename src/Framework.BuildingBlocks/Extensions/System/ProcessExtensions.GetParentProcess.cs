using System.Diagnostics;
using System.Runtime.Versioning;
using Framework.BuildingBlocks.Helpers;

namespace Framework.BuildingBlocks.Extensions.System;

public static partial class ProcessExtensions
{
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    public static int? GetParentProcessId(this Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        var processId = process.Id;

        if (OperatingSystem.IsWindows())
        {
            foreach (var entry in ProcessHelper.GetProcesses())
            {
                if (entry.ProcessId == processId)
                {
                    return entry.ParentProcessId;
                }
            }

            return null;
        }

        // Linux

        try
        {
            using var stream = File.OpenRead("/proc/" + processId.ToString(CultureInfo.InvariantCulture) + "/status");
            using var sr = new StreamReader(stream);

            while (sr.ReadLine() is { } line)
            {
                const string prefix = "PPid:";

                if (
                    line.StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(
                        line[prefix.Length..],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var ppid
                    )
                )
                {
                    return ppid;
                }
            }

            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    public static Process? GetParentProcess(this Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        var parentProcessId = GetParentProcessId(process);

        if (parentProcessId is null)
        {
            return null;
        }

        var parentProcess = Process.GetProcessById(parentProcessId.Value);

        if (parentProcess.StartTime > process.StartTime)
        {
            return null;
        }

        return parentProcess;
    }
}
