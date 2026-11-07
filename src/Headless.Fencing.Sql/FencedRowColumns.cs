// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>
/// The names of the five lease columns on a consumer's own table, unquoted and in the table's naming convention; the
/// descriptor quotes them, so a name never carries SQL.
/// </summary>
/// <remarks>
/// The columns hold <see cref="RowLeaseSnapshot" />: the generation (64-bit integer, not null, default 0), the state
/// (16-bit integer, not null, default 0, so a fresh row is <see cref="RowLeaseState.Idle" />), the expiry (an instant
/// with offset, null until the first grant), the holder (text of at most <c>FencingFieldLimits.HolderMaxLength</c>
/// characters, null), and the takeover count (32-bit integer, not null, default 0).
/// </remarks>
[PublicAPI]
public sealed class FencedRowColumns
{
    /// <summary>Gets the generation column's name. Defaults to <c>lease_generation</c>.</summary>
    public string Generation { get; init; } = "lease_generation";

    /// <summary>Gets the state column's name. Defaults to <c>lease_state</c>.</summary>
    public string State { get; init; } = "lease_state";

    /// <summary>Gets the expiry column's name. Defaults to <c>lease_expires_at</c>.</summary>
    public string ExpiresAt { get; init; } = "lease_expires_at";

    /// <summary>Gets the holder column's name. Defaults to <c>lease_holder</c>.</summary>
    public string Holder { get; init; } = "lease_holder";

    /// <summary>Gets the takeover count column's name. Defaults to <c>lease_takeover_count</c>.</summary>
    public string TakeoverCount { get; init; } = "lease_takeover_count";
}
