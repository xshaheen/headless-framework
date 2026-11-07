// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Fencing;

/// <summary>
/// The lease stored on a row, as an immutable value: the input and output of <see cref="RowLeaseRules" />, and what a
/// mapping of the five lease columns reads.
/// </summary>
/// <remarks>
/// <see langword="default" /> is a row that was never granted: generation 0, <see cref="RowLeaseState.Idle" />, no expiry, no
/// holder, and no takeovers, which is what the columns' defaults store.
/// </remarks>
/// <param name="Generation">The last generation granted on the row; 0 before the first grant.</param>
/// <param name="State">The last attempt's state.</param>
/// <param name="ExpiresAt">The last attempt's expiry, or <see langword="null" /> before the first grant.</param>
/// <param name="Holder">The owner tag the last grant stored, or <see langword="null" /> when it stored none.</param>
/// <param name="TakeoverCount">
/// How many times the row's work was taken from an expired attempt since an attempt last settled, was released, or
/// was revoked.
/// </param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct RowLeaseSnapshot(
    long Generation,
    RowLeaseState State,
    DateTimeOffset? ExpiresAt,
    string? Holder,
    int TakeoverCount
);
