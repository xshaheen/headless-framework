// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Tests.Oracle;

/// <summary>What one ledger entry records.</summary>
public enum FencingEventKind
{
    /// <summary>The lock granted a worker a lease carrying <see cref="FencingEvent.Token"/>.</summary>
    Granted,

    /// <summary>The protected resource accepted a write fenced by <see cref="FencingEvent.Token"/>.</summary>
    WriteAccepted,

    /// <summary>The protected resource rejected a write whose token was at or below its high-water mark.</summary>
    WriteRejected,

    /// <summary>The worker stopped holding the lease, whether the release reached the backend or not.</summary>
    Released,
}

/// <summary>
/// One entry of the side-effect log. <see cref="Sequence"/> orders every entry of a run. <see cref="HighWaterMark"/> is
/// the resource's highest accepted token at the moment that decides the entry: when the worker started to acquire for a
/// grant, and just before the write for a write.
/// </summary>
public sealed record FencingEvent(
    long Sequence,
    TimeSpan At,
    string Worker,
    string Resource,
    FencingEventKind Kind,
    long Token,
    long HighWaterMark
)
{
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"#{Sequence} {At.TotalSeconds:F3}s {Worker} {Resource} {Kind} token {Token} (high-water {HighWaterMark})"
        );
}

/// <summary>
/// The side-effect log of a lock chaos run: every grant, fenced write attempt, and release, in one order. Also counts
/// how many workers believe they hold each resource at once, which a connection death or a failover can push past one.
/// </summary>
public sealed class FencingLedger(TimeProvider clock)
{
    private readonly Lock _lock = new();
    private readonly List<FencingEvent> _events = [];
    private readonly Dictionary<string, int> _holders = new(StringComparer.Ordinal);
    private readonly long _start = clock.GetTimestamp();
    private int _maxConcurrentHolders;
    private int _overlappingHolds;

    /// <summary>The most workers that held one resource at the same time.</summary>
    public int MaxConcurrentHolders
    {
        get
        {
            lock (_lock)
            {
                return _maxConcurrentHolders;
            }
        }
    }

    /// <summary>How many grants started while another worker still held the same resource.</summary>
    public int OverlappingHolds
    {
        get
        {
            lock (_lock)
            {
                return _overlappingHolds;
            }
        }
    }

    /// <summary>Records a grant and counts its holder in.</summary>
    public void Granted(string worker, string resource, long token, long highWaterMarkAtAcquireStart)
    {
        lock (_lock)
        {
            var holders = _holders.GetValueOrDefault(resource) + 1;
            _holders[resource] = holders;

            if (holders > 1)
            {
                _overlappingHolds++;
            }

            _maxConcurrentHolders = Math.Max(_maxConcurrentHolders, holders);
            _Append(worker, resource, FencingEventKind.Granted, token, highWaterMarkAtAcquireStart);
        }
    }

    /// <summary>Records a release and counts its holder out.</summary>
    public void Released(string worker, string resource, long token)
    {
        lock (_lock)
        {
            _holders[resource] = _holders.GetValueOrDefault(resource) - 1;
            _Append(worker, resource, FencingEventKind.Released, token, highWaterMark: 0);
        }
    }

    /// <summary>Records a write attempt. The store calls it while it holds its own lock, so writes log in store order.</summary>
    public void Wrote(string worker, string resource, long token, bool accepted, long highWaterMarkBefore)
    {
        lock (_lock)
        {
            _Append(
                worker,
                resource,
                accepted ? FencingEventKind.WriteAccepted : FencingEventKind.WriteRejected,
                token,
                highWaterMarkBefore
            );
        }
    }

    /// <summary>A copy of every entry so far, in order.</summary>
    public IReadOnlyList<FencingEvent> Snapshot()
    {
        lock (_lock)
        {
            return [.. _events];
        }
    }

    private void _Append(string worker, string resource, FencingEventKind kind, long token, long highWaterMark)
    {
        _events.Add(
            new FencingEvent(_events.Count, clock.GetElapsedTime(_start), worker, resource, kind, token, highWaterMark)
        );
    }
}
