// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Security.Cryptography;
using Headless.Checks;

namespace Headless.Caching.Benchmarks;

internal static class BenchmarkPayloadFactory
{
    public static BenchmarkPayload Create(int sizeBytes, int seed)
    {
        Argument.IsPositive(sizeBytes);

        var bytes = new byte[sizeBytes];

#pragma warning disable CA5394 // Benchmark payload generation requires deterministic seeded data.
        var random = new Random(seed);
        random.NextBytes(bytes);
#pragma warning restore CA5394

        return new BenchmarkPayload(
            seed,
            bytes,
            Convert.ToBase64String(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{seed}:{sizeBytes}"))
                )
            )
        );
    }
}
