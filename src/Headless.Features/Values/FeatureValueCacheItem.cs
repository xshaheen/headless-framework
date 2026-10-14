// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Text;

namespace Headless.Features;

/// <summary>Cache entry that holds the raw string value of a feature for a specific provider/key combination.</summary>
public sealed class FeatureValueCacheItem(string? value)
{
    private static readonly CompositeFormat _Format = CompositeFormat.Parse("features:provider:{0}:{1},name:{2}");

    /// <summary>Gets the cached feature value, or <see langword="null"/> when no value has been set for this provider/key.</summary>
    public string? Value { get; } = value;

    /// <summary>Computes the cache key for the given feature <paramref name="name"/>, <paramref name="providerName"/>, and <paramref name="providerKey"/>.</summary>
    /// <param name="name">The feature name.</param>
    /// <param name="providerName">The provider name (e.g. <c>"Tenant"</c>).</param>
    /// <param name="providerKey">An optional key that qualifies the provider scope (e.g. a tenant identifier).</param>
    /// <returns>A deterministic cache key string.</returns>
    public static string CalculateCacheKey(string name, string? providerName, string? providerKey)
    {
        return string.Format(CultureInfo.InvariantCulture, _Format, providerName, providerKey, name);
    }

    /// <summary>Extracts the feature name from a cache key produced by <see cref="CalculateCacheKey"/>.</summary>
    /// <param name="cacheKey">The cache key to parse.</param>
    /// <returns>The feature name, or <see langword="null"/> if the key does not match the expected format.</returns>
    public static string? GetFeatureNameFromCacheKey(string cacheKey)
    {
        var result = FormattedStringValueExtractor.Extract(cacheKey, _Format.Format, ignoreCase: true);

        return result.IsMatch ? result.Matches[^1].Value : null;
    }
}
