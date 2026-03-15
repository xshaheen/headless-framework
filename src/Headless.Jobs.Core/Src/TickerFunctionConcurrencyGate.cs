// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Jobs.Interfaces;

namespace Headless.Jobs;

internal sealed class TickerFunctionConcurrencyGate : ITickerFunctionConcurrencyGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new(StringComparer.Ordinal);

    public SemaphoreSlim? GetSemaphoreOrNull(string functionName, int maxConcurrency)
    {
        if (maxConcurrency <= 0)
        {
            return null;
        }

        return _semaphores.GetOrAdd(functionName, _ => new SemaphoreSlim(maxConcurrency, maxConcurrency));
    }
}
