using Framework.Kernel.BuildingBlocks.Helpers.Text;

namespace Framework.Permissions.PermissionManagement;

public sealed class PermissionGrantCacheItem
{
    private const string _CacheKeyFormat = "pn:{0},pk:{1},n:{2}";

    public bool IsGranted { get; set; }

    public PermissionGrantCacheItem() { }

    public PermissionGrantCacheItem(bool isGranted)
    {
        IsGranted = isGranted;
    }

    public static string CalculateCacheKey(string name, string providerName, string? providerKey)
    {
        return string.Format(_CacheKeyFormat, providerName, providerKey, name);
    }

    public static string? GetPermissionNameFormCacheKeyOrDefault(string cacheKey)
    {
        var result = FormattedStringValueExtracter.Extract(cacheKey, _CacheKeyFormat, ignoreCase: true);

        return result.IsMatch ? result.Matches[^1].Value : null;
    }
}
