// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features.Values;

public sealed class FeatureValueCacheItem(string? value)
{
    private static readonly CompositeFormat _Format = CompositeFormat.Parse("features:provider:{0}:{1},name:{2}");

    public string? Value { get; } = value;

    public static string CalculateCacheKey(string name, string? providerName, string? providerKey)
    {
        return string.Format(CultureInfo.InvariantCulture, _Format, providerName, providerKey, name);
    }
}
