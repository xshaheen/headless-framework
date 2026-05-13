// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Constants;

namespace Headless.Messaging.OpenTelemetry;

internal static class MessagingDiagnostics
{
    public const string SourceName = HeadlessDiagnostics.Prefix + "Messaging";

    public static readonly ActivitySource ActivitySource = HeadlessDiagnostics.CreateActivitySource("Messaging");

    public static Meter CreateMeter()
    {
        return HeadlessDiagnostics.CreateMeter("Messaging");
    }

    public static Activity? Start(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(name, kind, default(ActivityContext));
    }

    public static Activity? Start(string name, ActivityKind kind, ActivityContext parentContext)
    {
        return ActivitySource.StartActivity(name, kind, parentContext);
    }
}
