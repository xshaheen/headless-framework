// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Headless.MultiTenancy;

/// <summary>
/// The rule a tenant id must pass before a storage feature places it in a path as one segment, such as
/// <c>{tenant}/{key}</c>. Every feature that keeps each tenant's data under its own segment applies this one rule,
/// so an id accepted by one is accepted by all.
/// </summary>
/// <remarks>
/// The id is checked, never encoded or normalized: a rewritten id could become another tenant's id. A feature
/// that reserves further names inside its own layout checks those itself.
/// </remarks>
[PublicAPI]
public static partial class TenantStorageSegment
{
    /// <summary>Whether <paramref name="tenantId"/> can be used as one storage path segment.</summary>
    /// <param name="tenantId">The tenant id to check.</param>
    /// <returns>
    /// <see langword="true"/> when the id starts and ends with a lowercase ASCII letter or digit and holds only
    /// lowercase ASCII letters, digits, <c>.</c>, <c>_</c>, and <c>-</c>; otherwise <see langword="false"/>,
    /// including for <see langword="null"/> and an empty id.
    /// </returns>
    public static bool IsValid([NotNullWhen(true)] string? tenantId)
    {
        // An allow-list rather than a deny-list: a segment the filesystem-like normalizers would strip or split
        // ('a:b' becomes 'ab', 'a/b' becomes two segments) would land one tenant inside another tenant's prefix.
        // Lowercase only, because a store on a case-insensitive disk (macOS, Windows) would put 'Acme' and 'acme'
        // in one directory; folding the case instead would merge the two tenants just the same.
        return tenantId is not null && SegmentRegex.IsMatch(tenantId);
    }

    // \z, not $: a dollar sign also matches before a final line feed, which would let "acme\n" through.
    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex SegmentRegex { get; }
}
