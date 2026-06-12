// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using System.Text;

namespace Headless.Caching.Benchmarks;

internal static class BenchmarkPayloadFactory
{
    public static BenchmarkPayload Create(int sizeBytes, int seed)
    {
        if (sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "Payload size must be positive.");
        }

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
