// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Text;

namespace Framework.Settings.Values;

public sealed class SettingValueCacheItem(string? value)
{
    private static readonly CompositeFormat _Format = CompositeFormat.Parse("settings:provider:{0}:{1},name:{2}");

    public string? Value { get; } = value;

    public static string CalculateCacheKey(string name, string providerName, string? providerKey)
    {
        return string.Format(CultureInfo.InvariantCulture, _Format, providerName, providerKey, name);
    }

    public static string? GetSettingNameFromCacheKey(string cacheKey)
    {
        var result = FormattedStringValueExtractor.Extract(cacheKey, _Format.Format, ignoreCase: true);

        return result.IsMatch ? result.Matches[^1].Value : null;
    }
}
