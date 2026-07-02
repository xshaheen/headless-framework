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

        return $"bench:{_Sanitize(providerId)}:{_Sanitize(scenario)}:{_Sanitize(runId)}:";
    }

    private static string _Sanitize(string value) => value.Replace(' ', '-').ToLowerInvariant();
}
