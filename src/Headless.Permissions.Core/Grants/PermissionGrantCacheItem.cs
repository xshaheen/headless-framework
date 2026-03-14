// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Text;

namespace Headless.Permissions.Grants;

public sealed class PermissionGrantCacheItem(bool? isGranted)
{
    private static readonly CompositeFormat _Format = CompositeFormat.Parse("permissions:t:{0},provider:{1}:{2},name:{3}");

    /// <summary>
    /// True = granted, False = explicit denial, null = undefined (no cached record).
    /// </summary>
    public bool? IsGranted { get; } = isGranted;

    public static string CalculateCacheKey(string name, string providerName, string? providerKey, string? tenantId)
    {
        return string.Format(CultureInfo.InvariantCulture, _Format, tenantId, providerName, providerKey, name);
    }

    public static string? GetPermissionNameFormCacheKeyOrDefault(string cacheKey)
    {
        var result = FormattedStringValueExtractor.Extract(cacheKey, _Format.Format, ignoreCase: true);

        return result.IsMatch ? result.Matches[^1].Value : null;
    }
}
