// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching.Benchmarks;

internal static class BenchmarkKeyPrefix
{
    public static string Create(string providerId, string scenario, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return $"bench:{Sanitize(providerId)}:{Sanitize(scenario)}:{Sanitize(runId)}:";
    }

    private static string Sanitize(string value) =>
        value.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant();
}
