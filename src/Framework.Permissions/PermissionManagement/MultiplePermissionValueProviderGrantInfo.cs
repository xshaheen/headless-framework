using Framework.Kernel.Checks;
using Framework.Permissions.Models;

namespace Framework.Permissions.PermissionManagement;

public sealed class MultiplePermissionValueProviderGrantInfo
{
    public Dictionary<string, PermissionValueProviderGrantInfo> Result { get; } = new(StringComparer.Ordinal);

    public MultiplePermissionValueProviderGrantInfo() { }

    public MultiplePermissionValueProviderGrantInfo(string[] names)
    {
        Argument.IsNotNull(names);

        foreach (var name in names)
        {
            Result.Add(name, PermissionValueProviderGrantInfo.NonGranted);
        }
    }
}
