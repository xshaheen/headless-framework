// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Constants;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

/// <summary>
/// Centralises the <see cref="ActivitySource"/> used by the Jobs subsystem. Consumers subscribe via
/// <see cref="SourceName"/> (<c>AddSource</c>) or the typed <c>AddJobsInstrumentation()</c> extension
/// on <c>TracerProviderBuilder</c>.
/// </summary>
[PublicAPI]
public static class JobsDiagnostics
{
    /// <summary>The full activity-source name used by the Jobs subsystem.</summary>
    public const string SourceName = HeadlessDiagnostics.Prefix + "Jobs";

    internal static readonly ActivitySource ActivitySource = HeadlessDiagnostics.CreateActivitySource("Jobs");

    internal static Activity? Start(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(name, kind, default(ActivityContext));
    }
}
