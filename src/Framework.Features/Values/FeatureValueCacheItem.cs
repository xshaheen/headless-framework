// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Features.Values;

[Serializable]
public class FeatureValueCacheItem(string? value)
{
    public string? Value { get; set; } = value;

    public static string CalculateCacheKey(string name, string? providerName, string? providerKey)
    {
        return "pn:" + providerName + ",pk:" + providerKey + ",n:" + name;
    }
}
