// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Framework.Constants;

public static class FrameworkDiagnostics
{
    public static ActivitySource ActivitySource { get; }

    public static Meter Meter { get; }

    static FrameworkDiagnostics()
    {
        var packageVersion = typeof(FrameworkDiagnostics).Assembly.GetAssemblyVersion();

        ActivitySource = new("Framework", packageVersion);
        Meter = new("Framework", packageVersion);
    }
}
