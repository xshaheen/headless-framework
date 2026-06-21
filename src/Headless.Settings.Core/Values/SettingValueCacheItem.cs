// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Text;

namespace Headless.Settings.Values;

/// <summary>Cached representation of a single setting value for a specific provider and key.</summary>
public sealed class SettingValueCacheItem(string? value)
{
    private static readonly CompositeFormat _Format = CompositeFormat.Parse("settings:provider:{0}:{1},name:{2}");

    /// <summary>Gets the cached setting value, or <see langword="null"/> if no value was stored.</summary>
    public string? Value { get; } = value;

    /// <summary>Computes the cache key for a setting value entry.</summary>
    /// <param name="name">The setting name.</param>
    /// <param name="providerName">The provider name (e.g. <c>Global</c>, <c>Tenant</c>).</param>
    /// <param name="providerKey">The provider-scoped key, or <see langword="null"/> for global providers.</param>
    /// <returns>A deterministic cache key string.</returns>
    public static string CalculateCacheKey(string name, string providerName, string? providerKey)
    {
        return string.Format(CultureInfo.InvariantCulture, _Format, providerName, providerKey, name);
    }

    /// <summary>Extracts the setting name from a cache key produced by <see cref="CalculateCacheKey"/>.</summary>
    /// <param name="cacheKey">The cache key to parse.</param>
    /// <returns>The setting name, or <see langword="null"/> if the key does not match the expected format.</returns>
    public static string? GetSettingNameFromCacheKey(string cacheKey)
    {
        var result = FormattedStringValueExtractor.Extract(cacheKey, _Format.Format, ignoreCase: true);

        return result.IsMatch ? result.Matches[^1].Value : null;
    }
}
