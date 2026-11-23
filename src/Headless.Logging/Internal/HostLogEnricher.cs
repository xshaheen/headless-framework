// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Reflection;
using Microsoft.Extensions.Diagnostics.Enrichment;

namespace Headless.Logging.Internal;

/// <summary>Adds the machine name, process name, and the entry assembly's commit to every entry.</summary>
/// <remarks>
/// These are the host facts Microsoft's process and application enrichers do not add.
/// </remarks>
internal sealed class HostLogEnricher : IStaticLogEnricher
{
    internal const string MachineName = "MachineName";
    internal const string ProcessName = "ProcessName";
    internal const string CommitHash = "CommitHash";

    private readonly string _processName = _ReadProcessName();
    private readonly string? _commitHash = AssemblyInformation.Entry?.CommitNumber;

    public void Enrich(IEnrichmentTagCollector collector)
    {
        collector.Add(MachineName, Environment.MachineName);
        collector.Add(ProcessName, _processName);

        if (!string.IsNullOrEmpty(_commitHash))
        {
            collector.Add(CommitHash, _commitHash);
        }
    }

    private static string _ReadProcessName()
    {
        using var process = Process.GetCurrentProcess();

        return process.ProcessName;
    }
}
