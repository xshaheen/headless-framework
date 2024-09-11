using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

// ReSharper disable once CheckNamespace
namespace Framework.Kernel.BuildingBlocks;

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
