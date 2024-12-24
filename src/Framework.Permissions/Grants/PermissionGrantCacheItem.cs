// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Text;

namespace Framework.Permissions.Grants;

public sealed class PermissionGrantCacheItem(bool isGranted)
{
    private static readonly CompositeFormat _CacheKeyFormat = CompositeFormat.Parse("pn:{0},pk:{1},n:{2}");

    public bool IsGranted { get; } = isGranted;

    public static string CalculateCacheKey(string name, string providerName, string? providerKey)
    {
        return string.Format(CultureInfo.InvariantCulture, _CacheKeyFormat, providerName, providerKey, name);
    }

    public static string? GetPermissionNameFormCacheKeyOrDefault(string cacheKey)
    {
        var result = FormattedStringValueExtracter.Extract(cacheKey, _CacheKeyFormat.Format, ignoreCase: true);

        return result.IsMatch ? result.Matches[^1].Value : null;
    }
}
