// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Dashboard;

namespace Headless.Cluster;

/// <summary>The object kinds the Cluster module shows, and how they read as the shell's references and versions.</summary>
internal static class ClusterObjects
{
    /// <summary>A node incarnation, by its <c>node@incarnation</c> identity; its stream topic is <c>cluster.node</c>.</summary>
    public const string NodeKind = "node";

    /// <summary>A health check, by its registration name; its stream topic is <c>cluster.check</c>.</summary>
    public const string CheckKind = "check";

    /// <summary>The serving host as a whole, by its label; only the probe-all command acts on it.</summary>
    public const string HealthKind = "health";

    public static ObjectRef Ref(string moduleId, string kind, string id) =>
        new()
        {
            Module = moduleId,
            Kind = kind,
            Id = id,
        };

    /// <summary>
    /// A change version from an instant: its UTC ticks, zero-padded to 19 digits. The shell keeps a change only when
    /// its version is newer than the last it sent for the object, and compares versions as integers, so a later instant
    /// always passes, including a flip back to an earlier state.
    /// </summary>
    public static string Version(DateTimeOffset at) => at.UtcTicks.ToString("D19", CultureInfo.InvariantCulture);
}
