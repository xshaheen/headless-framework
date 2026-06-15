// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Caching.Benchmarks;

internal static class BenchmarkKeyPrefix
{
    public static string Create(string providerId, string scenario, string runId)
    {
        Argument.IsNotNullOrWhiteSpace(providerId);
        Argument.IsNotNullOrWhiteSpace(scenario);
        Argument.IsNotNullOrWhiteSpace(runId);

        return $"bench:{Sanitize(providerId)}:{Sanitize(scenario)}:{Sanitize(runId)}:";
    }

    private static string Sanitize(string value) =>
        value.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant();
}
