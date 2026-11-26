// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>One page of an <see cref="IFencedLeaseInspector" /> list.</summary>
/// <remarks>This record grows by <see langword="init" /> members.</remarks>
[PublicAPI]
public sealed record FencedLeasePage
{
    /// <summary>The most leases one page holds.</summary>
    public const int MaxPageSize = 500;

    /// <summary>Gets the leases, in the inspector's order. None carries its progress bytes.</summary>
    public required IReadOnlyList<LeaseSnapshot> Items { get; init; }

    /// <summary>Gets the position to read the next page after, or <see langword="null" /> when this page is the last.</summary>
    public FencedLeasePosition? Next { get; init; }
}
