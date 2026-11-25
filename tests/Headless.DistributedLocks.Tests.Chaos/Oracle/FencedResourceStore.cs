// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;

namespace Tests.Oracle;

/// <summary>
/// The protected resource of the documented consumer pattern: per resource name it keeps the highest
/// <see cref="LockFencingToken"/> it has accepted and rejects any write whose token is less than or equal to it.
/// Every attempt goes to the <see cref="FencingLedger"/>.
/// </summary>
public sealed class FencedResourceStore(FencingLedger ledger)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _highest = [with(StringComparer.Ordinal)];

    /// <summary>The highest token accepted on <paramref name="resource"/>, or 0 before the first write.</summary>
    public long HighWaterMark(string resource)
    {
        lock (_lock)
        {
            return _highest.GetValueOrDefault(resource);
        }
    }

    /// <summary>Writes when <paramref name="token"/> is above the high-water mark; returns whether it was accepted.</summary>
    public bool TryWrite(string worker, string resource, LockFencingToken token)
    {
        lock (_lock)
        {
            var highest = _highest.GetValueOrDefault(resource);
            var accepted = token.Value > highest;

            if (accepted)
            {
                _highest[resource] = token.Value;
            }

            ledger.Wrote(worker, resource, token.Value, accepted, highest);

            return accepted;
        }
    }
}
