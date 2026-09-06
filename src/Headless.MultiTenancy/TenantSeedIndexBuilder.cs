// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// Shared TryAdd-then-throw seed indexing used by <see cref="InMemoryTenantStore"/> and
/// <see cref="ConfigurationTenantStore"/>: builds the immutable id and normalized-identifier lookup
/// tables from already-converted, already-normalized <see cref="TenantInfo"/> entries (R20), rejecting
/// the first duplicate seen on either axis.
/// </summary>
internal static class TenantSeedIndexBuilder
{
    /// <summary>Builds the id and normalized-identifier lookup dictionaries from <paramref name="entries"/>.</summary>
    /// <param name="entries">
    /// The converted tenants to index, in seed order, each paired with the raw (pre-normalization) seed
    /// identifier it was built from — surfaced in the duplicate-identifier exception message.
    /// </param>
    /// <param name="storeDisplayName">The store name embedded in exception messages (for example <c>"in-memory"</c> or <c>"configuration"</c>).</param>
    /// <returns>The id-keyed and normalized-identifier-keyed lookup dictionaries, in seed order.</returns>
    /// <exception cref="InvalidOperationException">
    /// Two or more entries normalize to the same identifier, or two or more entries share the same id.
    /// </exception>
    public static (
        IReadOnlyDictionary<string, TenantInfo> ById,
        IReadOnlyDictionary<string, TenantInfo> ByNormalizedIdentifier
    ) Build(IReadOnlyCollection<(string RawIdentifier, TenantInfo Tenant)> entries, string storeDisplayName)
    {
        var byId = new Dictionary<string, TenantInfo>(entries.Count, StringComparer.Ordinal);
        var byIdentifier = new Dictionary<string, TenantInfo>(entries.Count, StringComparer.Ordinal);

        foreach (var (rawIdentifier, tenant) in entries)
        {
            if (!byIdentifier.TryAdd(tenant.Identifier, tenant))
            {
                throw new InvalidOperationException(
                    $"Headless.MultiTenancy {storeDisplayName} store: duplicate tenant identifier "
                        + $"'{tenant.Identifier}' (from seed identifier '{rawIdentifier}'). "
                        + "Seeded identifiers must be unique after normalization (R20)."
                );
            }

            if (!byId.TryAdd(tenant.Id, tenant))
            {
                throw new InvalidOperationException(
                    $"Headless.MultiTenancy {storeDisplayName} store: duplicate tenant id '{tenant.Id}'. "
                        + "Seeded tenant ids must be unique."
                );
            }
        }

        return (byId, byIdentifier);
    }
}
