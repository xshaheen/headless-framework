// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Constants;

namespace Headless.Jobs;

internal static class JobsDiagnostics
{
    public const string SourceName = HeadlessDiagnostics.Prefix + "Jobs";

    public static readonly ActivitySource ActivitySource = HeadlessDiagnostics.CreateActivitySource("Jobs");

    public static Activity? Start(string name, ActivityKind kind = ActivityKind.Internal) =>
        ActivitySource.StartActivity(name, kind, default(ActivityContext));
}
