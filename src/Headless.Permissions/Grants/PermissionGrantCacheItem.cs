// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Text;

namespace Headless.Permissions;

/// <summary>
/// Cache value that records the three-state grant status for a single (permission, provider, providerKey) tuple:
/// <see langword="true"/> = Granted, <see langword="false"/> = Prohibited (explicit denial),
/// <see langword="null"/> = Undefined (no record in the store).
/// </summary>
public sealed record PermissionGrantCacheItem
{
    private static readonly CompositeFormat _Format = CompositeFormat.Parse("permissions:provider:{0}:{1},name:{2}");

    /// <summary>Creates an entry holding <paramref name="isGranted"/> under a new <see cref="Stamp"/>.</summary>
    public PermissionGrantCacheItem(bool? isGranted)
    {
        IsGranted = isGranted;
    }

    /// <summary>
    /// True = granted, False = explicit denial, null = undefined (no cached record).
    /// </summary>
    public bool? IsGranted { get; }

    /// <summary>
    /// Tells this write apart from every other write of an equal value. A writer swaps an entry only while it still
    /// holds the exact write it read, so a slow writer cannot overwrite a newer entry that happens to hold the value it
    /// read. A new entry gets a random stamp; deserialization restores the stored one.
    /// </summary>
    public Guid Stamp { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Computes the cache key for a (name, providerName, providerKey) tuple.
    /// The key format embeds all three components so it is unique across tenants when the cache is already scoped.
    /// </summary>
    public static string CalculateCacheKey(string name, string providerName, string? providerKey)
    {
        return string.Format(CultureInfo.InvariantCulture, _Format, providerName, providerKey, name);
    }

    /// <summary>
    /// Extracts the permission name from a key produced by <see cref="CalculateCacheKey"/>, or
    /// returns <see langword="null"/> if the key does not match the expected format.
    /// </summary>
    public static string? GetPermissionNameFormCacheKeyOrDefault(string cacheKey)
    {
        var result = FormattedStringValueExtractor.Extract(cacheKey, _Format.Format, ignoreCase: true);

        return result.IsMatch ? result.Matches[^1].Value : null;
    }
}
