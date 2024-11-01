using Framework.Kernel.Checks;

namespace Framework.Permissions.Models;

public sealed class PermissionWithGrantedProviders(string name, bool isGranted)
{
    public string Name { get; } = Argument.IsNotNull(name);

    public bool IsGranted { get; set; } = isGranted;

    public List<PermissionValueProviderInfo> Providers { get; set; } = [];
}
