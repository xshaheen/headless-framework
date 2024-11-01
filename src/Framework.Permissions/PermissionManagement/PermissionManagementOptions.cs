using Framework.Kernel.Primitives;

namespace Framework.Permissions.PermissionManagement;

public sealed class PermissionManagementOptions
{
    public TypeList<IPermissionManagementProvider> ManagementProviders { get; } = [];

    public Dictionary<string, string> ProviderPolicies { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Default: true.
    /// </summary>
    public bool SaveStaticPermissionsToDatabase { get; set; } = true;

    /// <summary>
    /// Default: false.
    /// </summary>
    public bool IsDynamicPermissionStoreEnabled { get; set; }
}
