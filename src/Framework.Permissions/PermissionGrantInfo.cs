using Framework.Kernel.Checks;

namespace Framework.Permissions;

public sealed class PermissionGrantInfo(
    string name,
    bool isGranted,
    string? providerName = null,
    string? providerKey = null
)
{
    public string Name { get; } = Argument.IsNotNull(name);

    public bool IsGranted { get; } = isGranted;

    public string? ProviderName { get; } = providerName;

    public string? ProviderKey { get; } = providerKey;
}
