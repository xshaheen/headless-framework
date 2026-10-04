// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.DistributedLocks;

namespace Tests.Fakes;

/// <summary>
/// A controllable <see cref="IDbSynchronizationStrategy{TLockCookie}"/> for exercising the multiplexing engine's
/// share-vs-dedicate decision without a real database. Acquires succeed unless the resolved identity is already held on
/// the target connection (modelling PostgreSQL advisory re-entrancy on a session) or a test forces a failure; releases
/// can be made to throw to exercise the release-failure path.
/// </summary>
internal sealed class FakeSynchronizationStrategy : IDbSynchronizationStrategy<object>
{
    // (connection id, resolved identity) -> held. Models the per-session advisory state in the database.
    private readonly ConcurrentDictionary<(Guid Connection, object Identity), byte> _held = new();

    // Maps a resource string to its resolved physical-lock identity. Two strings mapping to the same value model an
    // advisory-key collision (ASCII/int overlap or SHA hash collision).
    private readonly Dictionary<string, object> _identityByResource = new(StringComparer.Ordinal);

    public bool IsUpgradeable { get; init; }

    /// <summary>When set, acquires on this connection id fail (return null) regardless of held state.</summary>
    public Guid? FailAcquireOnConnection { get; set; }

    /// <summary>When set, releasing this identity throws — used to exercise the release-failure path.</summary>
    public object? ThrowOnReleaseIdentity { get; set; }

    public int AcquireCount { get; private set; }

    public int ReleaseCount { get; private set; }

    /// <summary>Maps <paramref name="resource"/> to <paramref name="identity"/> for collision modelling.</summary>
    public void MapIdentity(string resource, object identity)
    {
        _identityByResource[resource] = identity;
    }

    public object GetHeldLockIdentity(string resourceName)
    {
        return _identityByResource.TryGetValue(resourceName, out var identity) ? identity : resourceName;
    }

    public ValueTask<object?> TryAcquireAsync(
        DatabaseConnection connection,
        string resourceName,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        AcquireCount++;

        var id = ((RecordingDatabaseConnection)connection).Id;
        var identity = GetHeldLockIdentity(resourceName);

        if (FailAcquireOnConnection == id)
        {
            return ValueTask.FromResult<object?>(null);
        }

        // The engine's held-set check already prevents the same identity being granted twice on one connection, so we
        // model a successful grant here and record it.
        _held[(id, identity)] = 1;

        return ValueTask.FromResult<object?>(identity);
    }

    public ValueTask ReleaseAsync(DatabaseConnection connection, string resourceName, object lockCookie)
    {
        ReleaseCount++;

        var id = ((RecordingDatabaseConnection)connection).Id;

        if (ThrowOnReleaseIdentity is not null && Equals(ThrowOnReleaseIdentity, lockCookie))
        {
            throw new InvalidOperationException("Simulated release failure.");
        }

        _held.TryRemove((id, lockCookie), out _);

        return ValueTask.CompletedTask;
    }

    /// <summary><see langword="true"/> if the given identity is recorded as held on the given connection id.</summary>
    public bool IsHeld(Guid connectionId, object identity)
    {
        return _held.ContainsKey((connectionId, identity));
    }
}
