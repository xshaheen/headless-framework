// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Checks;

namespace Headless.Messaging.Persistence;

/// <summary>
/// When a retry transition makes its row due again, given as a delay the store adds to its own clock. The store
/// decides when the row is due by that same clock, so an application clock skewed from the store's cannot make the
/// retry fire early or late.
/// </summary>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct RetryDelay
{
    private RetryDelay(TimeSpan delay, bool keepsLaterDue)
    {
        Delay = delay;
        KeepsLaterDue = keepsLaterDue;
    }

    /// <summary>Gets how long after the store's current time the row falls due.</summary>
    public TimeSpan Delay { get; }

    /// <summary>
    /// Gets whether a due time already on the row survives the write when it is later than the store's current time
    /// plus <see cref="Delay"/>.
    /// </summary>
    public bool KeepsLaterDue { get; }

    /// <summary>The row falls due <paramref name="delay"/> after the store's current time.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> is negative.</exception>
    public static RetryDelay Exactly(TimeSpan delay) => new(Argument.IsPositiveOrZero(delay), keepsLaterDue: false);

    /// <summary>
    /// The row falls due no sooner than <paramref name="delay"/> after the store's current time, and keeps a later due
    /// time it already has.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> is negative.</exception>
    public static RetryDelay AtLeast(TimeSpan delay) => new(Argument.IsPositiveOrZero(delay), keepsLaterDue: true);

    /// <summary>
    /// Resolves the due time for a store that keeps its clock in process, such as one built on a
    /// <see cref="TimeProvider"/>. A relational store computes the same value in its statement instead.
    /// </summary>
    /// <param name="storeNow">The store's current time.</param>
    /// <param name="currentDue">The due time already on the row, if any.</param>
    public DateTimeOffset ResolveDueAt(DateTimeOffset storeNow, DateTimeOffset? currentDue)
    {
        var due = storeNow.Add(Delay);

        return KeepsLaterDue && currentDue > due ? currentDue.Value : due;
    }
}
