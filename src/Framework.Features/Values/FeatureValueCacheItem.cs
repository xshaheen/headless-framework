// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Features.Values;

public sealed class FeatureValueCacheItem(string? value)
{
    public string? Value { get; } = value;

    public static string CalculateCacheKey(string name, string? providerName, string? providerKey)
    {
        return "pn:" + providerName + ",pk:" + providerKey + ",n:" + name;
    }
}
