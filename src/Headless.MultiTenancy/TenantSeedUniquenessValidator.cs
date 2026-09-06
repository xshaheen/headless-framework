// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// Shared duplicate check used by <see cref="InMemoryTenantStoreOptionsValidator"/> and
/// <see cref="ConfigurationTenantStoreOptionsValidator"/>: true when every projected value is unique
/// (compared ordinally). Each validator supplies its own selector, so identifier normalization
/// (<c>Trim().ToLowerInvariant()</c>) stays scoped to the identifier check only — the id check never
/// normalizes.
/// </summary>
internal static class TenantSeedUniquenessValidator
{
    /// <summary>Whether every value projected from <paramref name="items"/> by <paramref name="selector"/> is unique.</summary>
    /// <param name="items">The seed collection to check.</param>
    /// <param name="selector">Projects each item to the string compared for uniqueness.</param>
    public static bool HaveUniqueValues<T>(ICollection<T> items, Func<T, string> selector)
    {
        return items.Select(selector).Distinct(StringComparer.Ordinal).Take(items.Count + 1).Count() == items.Count;
    }
}
