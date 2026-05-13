// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Headless.Constants;

/// <summary>
/// Factory for Headless framework diagnostic primitives. Each package should call
/// <see cref="CreateActivitySource"/> and <see cref="CreateMeter"/> to obtain its own
/// instance, enabling consumers to subscribe selectively per subsystem.
/// </summary>
[PublicAPI]
public static class HeadlessDiagnostics
{
    /// <summary>Shared name prefix applied to every Headless ActivitySource and Meter.</summary>
    public const string Prefix = "Headless.";

    private static readonly string _Version = typeof(HeadlessDiagnostics).Assembly.GetAssemblyVersion() ?? "1.0.0";

    /// <summary>Creates an <see cref="ActivitySource"/> named <c>Headless.{name}</c>.</summary>
    public static ActivitySource CreateActivitySource(string name) => new(Prefix + name, _Version);

    /// <summary>Creates a <see cref="Meter"/> named <c>Headless.{name}</c>.</summary>
    public static Meter CreateMeter(string name) => new(Prefix + name, _Version);
}
