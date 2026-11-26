// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Fencing;

/// <summary>A count that stops at a cap: a count above it reports the cap with <see cref="Capped" /> set.</summary>
/// <param name="Count">The count, at most the cap.</param>
/// <param name="Capped">Whether more leases matched than the cap.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct LeaseCount(int Count, bool Capped)
{
    /// <summary>The cap an operator view counts to: a larger count only says "more than this many".</summary>
    public const int DefaultCap = 10_000;

    /// <summary>Returns the count of <paramref name="matched" /> leases stopped at <paramref name="cap" />.</summary>
    /// <param name="matched">How many leases the store counted, which may exceed the cap by the one that proves it.</param>
    /// <param name="cap">The cap.</param>
    /// <returns>The capped count.</returns>
    public static LeaseCount Of(long matched, int cap)
    {
        return matched > cap ? new LeaseCount(cap, Capped: true) : new LeaseCount((int)matched, Capped: false);
    }
}
