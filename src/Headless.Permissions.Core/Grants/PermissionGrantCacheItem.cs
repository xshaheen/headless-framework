// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Text;

namespace Headless.Permissions.Grants;

/// <summary>
/// Cache value that records the three-state grant status for a single (permission, provider, providerKey) tuple:
/// <see langword="true"/> = Granted, <see langword="false"/> = Prohibited (explicit denial),
/// <see langword="null"/> = Undefined (no record in the store).
/// </summary>
public sealed class PermissionGrantCacheItem(bool? isGranted)
{
    private static readonly CompositeFormat _Format = CompositeFormat.Parse("permissions:provider:{0}:{1},name:{2}");

    /// <summary>
    /// True = granted, False = explicit denial, null = undefined (no cached record).
    /// </summary>
    public bool? IsGranted { get; } = isGranted;

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
