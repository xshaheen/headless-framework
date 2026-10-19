// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Capabilities;

/// <summary>
/// Proves the scheduled run's drift report can tell simulator drift from a provider failure. Setting
/// <see cref="ScenarioVariable"/> to a scenario name makes every real-broker leaf that requires that scenario fail
/// while the simulator's leaves keep passing, which is what a simulator that drifted from the real brokers looks
/// like. Only a dispatched drill run sets it; no default run does.
/// </summary>
[PublicAPI]
public static class ConformanceDriftDrill
{
    /// <summary>The environment variable naming the scenario whose real-broker leaves the drill fails.</summary>
    public const string ScenarioVariable = "HEADLESS_CONFORMANCE_DRIFT_DRILL";

    /// <summary>Fails the calling test when the drill targets <paramref name="scenario"/> on a real broker.</summary>
    /// <exception cref="InvalidOperationException">The drill targets this scenario on this real broker.</exception>
    public static void FailIfDrilled(string provider, TransportConformanceScenario scenario)
    {
        if (IsDrilled(Environment.GetEnvironmentVariable(ScenarioVariable), provider, scenario))
        {
            throw new InvalidOperationException(
                $"Drift drill: {provider} {scenario} fails on purpose because {ScenarioVariable}={scenario}."
            );
        }
    }

    /// <summary>
    /// Returns whether a drill naming <paramref name="drilledScenario"/> fails <paramref name="scenario"/> on
    /// <paramref name="provider"/>: only a manifest provider with an enabled real-broker leaf is drilled.
    /// </summary>
    public static bool IsDrilled(string? drilledScenario, string provider, TransportConformanceScenario scenario)
    {
        return !string.IsNullOrWhiteSpace(drilledScenario)
            && string.Equals(drilledScenario.Trim(), scenario.ToString(), StringComparison.Ordinal)
            && TransportConformanceManifest.Providers.TryGetValue(provider, out var profile)
            && profile.IsRealBrokerLeafEnabled;
    }
}
