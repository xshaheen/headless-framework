// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using Microsoft.Extensions.Options;

namespace Headless.MultiTenancy;

/// <summary>
/// Precomputes <see cref="TenantCatalogOptions.IgnoredIdentifiers"/> into a <see cref="FrozenSet{T}"/> of
/// normalized (trimmed, lowercased) entries, so the hot <see cref="TenantCatalogService.ResolveAsync"/>
/// path tests membership in O(1) instead of scanning the configured list on every request. Registered as
/// a singleton; materialization is deferred to first access (rather than the constructor) so it always
/// reflects the fully-configured <see cref="IOptions{TOptions}"/> snapshot regardless of DI resolution
/// timing.
/// </summary>
internal sealed class TenantCatalogIgnoredIdentifierSet(IOptions<TenantCatalogOptions> options)
{
    private readonly Lazy<FrozenSet<string>> _ignored = new(() =>
        options
            .Value.IgnoredIdentifiers.Select(static identifier => identifier.Trim().ToLowerInvariant())
            .ToFrozenSet(StringComparer.Ordinal)
    );

    /// <summary>
    /// Whether <paramref name="normalizedIdentifier"/> — already trimmed and lowercased by the caller —
    /// is configured as an ignored identifier. Ordinal comparison against lowercased entries reproduces
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> semantics against the already-normalized input.
    /// </summary>
    public bool Contains(string normalizedIdentifier) => _ignored.Value.Contains(normalizedIdentifier);
}
