// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Jobs.Interfaces;

namespace Headless.Jobs;

internal sealed class JobFunctionConcurrencyGate : IJobFunctionConcurrencyGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new(StringComparer.Ordinal);

    public SemaphoreSlim? GetSemaphoreOrNull(string functionName, int maxConcurrency)
    {
        if (maxConcurrency <= 0)
        {
            return null;
        }

        return _semaphores.GetOrAdd(functionName, static (_, max) => new SemaphoreSlim(max, max), maxConcurrency);
    }
}
