// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Checks;

namespace Headless.Caching.Benchmarks;

internal static class BenchmarkPayloadFactory
{
    public static BenchmarkPayload Create(int sizeBytes, int seed)
    {
        Argument.IsPositive(sizeBytes);

        var random = new Random(seed);
        var bytes = new byte[sizeBytes];
        random.NextBytes(bytes);

        return new BenchmarkPayload(
            seed,
            bytes,
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{sizeBytes}")))
        );
    }
}
