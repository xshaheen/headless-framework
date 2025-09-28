// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Text;

namespace Framework.Permissions.Grants;

public sealed class PermissionGrantCacheItem(bool isGranted)
{
    private static readonly CompositeFormat _Format = CompositeFormat.Parse("permissions:provider:{0}:{1},name:{2}");

    public bool IsGranted { get; } = isGranted;

    public static string CalculateCacheKey(string name, string providerName, string? providerKey)
    {
        return string.Format(CultureInfo.InvariantCulture, _Format, providerName, providerKey, name);
    }

    public static string? GetPermissionNameFormCacheKeyOrDefault(string cacheKey)
    {
        var result = FormattedStringValueExtractor.Extract(cacheKey, _Format.Format, ignoreCase: true);

        return result.IsMatch ? result.Matches[^1].Value : null;
    }
}
