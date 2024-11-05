using Framework.Kernel.Checks;
using Framework.Permissions.Models;
using Framework.Permissions.Results;

namespace Framework.Permissions.Values;

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
