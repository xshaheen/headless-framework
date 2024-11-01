namespace Framework.Permissions.Models;

public sealed class PermissionValueProviderGrantInfo(bool isGranted, string? providerKey = null) //TODO: Rename to PermissionGrantInfo
{
    public static PermissionValueProviderGrantInfo NonGranted { get; } = new(isGranted: false);

    public bool IsGranted { get; } = isGranted;

    public string? ProviderKey { get; } = providerKey;
}
