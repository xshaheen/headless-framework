// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Text;
using Framework.Kernel.BuildingBlocks.Helpers.Text;

namespace Framework.Settings.ValuesStorage;

public sealed class SettingCacheItem(string? value)
{
    public string? Value { get; set; } = value;

    private static readonly CompositeFormat _CacheKeyFormat = CompositeFormat.Parse("pn:{0},pk:{1},n:{2}");

    public static string CalculateCacheKey(string name, string? providerName, string? providerKey)
    {
        return string.Format(CultureInfo.InvariantCulture, _CacheKeyFormat, providerName, providerKey, name);
    }

    public static string? GetSettingNameFormCacheKey(string cacheKey)
    {
        var result = FormattedStringValueExtracter.Extract(cacheKey, _CacheKeyFormat.Format, ignoreCase: true);

        return result.IsMatch ? result.Matches[^1].Value : null;
    }
}
