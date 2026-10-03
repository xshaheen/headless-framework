// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;

namespace Headless.DistributedLocks;

/// <summary>
/// A fencing token issued by a distributed-lock backend when it grants a lock or semaphore slot. The protected
/// resource stores the highest token it has accepted and rejects any write whose token is less than or equal to it.
/// </summary>
/// <remarks>
/// <para>
/// Tokens come from the lock backend's own sequence: the <c>headless_distributed_locks_fence</c> database sequence
/// for PostgreSQL and SQL Server (one sequence shared by every resource, so a resource's tokens increase but skip
/// values), a per-resource counter for Redis, and an in-process per-resource counter for InMemory. Every issuer starts
/// at 1.
/// </para>
/// <para>
/// A token must never be compared with a <c>Headless.Fencing</c> lease generation (<c>FencedLease.Generation</c>).
/// Generations come from an unrelated store-wide sequence, so comparing the two silently rejects valid writes or
/// accepts stale ones. This type deliberately has no conversion to or from <see cref="long"/> and compares only with
/// its own kind, so such a comparison does not compile. A resource guarded by both a lock and a lease must record the
/// highest token and the highest generation in separate fields.
/// </para>
/// <para>
/// Use <see cref="Value"/> to persist a token and <see cref="LockFencingToken(long)"/> to read one back.
/// </para>
/// </remarks>
[PublicAPI]
public readonly struct LockFencingToken : IEquatable<LockFencingToken>, IComparable<LockFencingToken>
{
    /// <summary>Creates a token from a value previously read from <see cref="Value"/>.</summary>
    /// <param name="value">The raw token value; always positive because every issuer starts at 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is zero or negative.</exception>
    public LockFencingToken(long value)
    {
        Argument.IsPositive(value);
        Value = value;
    }

    /// <summary>
    /// The raw token value, for persisting at the protected resource. Compare tokens with each other, not their raw
    /// values with numbers from another sequence.
    /// </summary>
    public long Value { get; }

    /// <inheritdoc/>
    public bool Equals(LockFencingToken other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LockFencingToken other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc/>
    public int CompareTo(LockFencingToken other) => Value.CompareTo(other.Value);

    /// <summary>Returns the raw token value formatted with the invariant culture.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Determines whether two tokens are equal.</summary>
    public static bool operator ==(LockFencingToken left, LockFencingToken right) => left.Equals(right);

    /// <summary>Determines whether two tokens differ.</summary>
    public static bool operator !=(LockFencingToken left, LockFencingToken right) => !left.Equals(right);

    /// <summary>Determines whether <paramref name="left"/> was issued before <paramref name="right"/>.</summary>
    public static bool operator <(LockFencingToken left, LockFencingToken right) => left.Value < right.Value;

    /// <summary>Determines whether <paramref name="left"/> was issued before or is <paramref name="right"/>.</summary>
    public static bool operator <=(LockFencingToken left, LockFencingToken right) => left.Value <= right.Value;

    /// <summary>Determines whether <paramref name="left"/> was issued after <paramref name="right"/>.</summary>
    public static bool operator >(LockFencingToken left, LockFencingToken right) => left.Value > right.Value;

    /// <summary>Determines whether <paramref name="left"/> was issued after or is <paramref name="right"/>.</summary>
    public static bool operator >=(LockFencingToken left, LockFencingToken right) => left.Value >= right.Value;
}
