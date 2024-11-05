using Framework.Kernel.Checks;
using Framework.Permissions.Models;
using Framework.Permissions.Results;

namespace Framework.Permissions.Values;

public sealed class MultiplePermissionWithGrantedProviders
{
    public List<PermissionWithGrantedProviders> Result { get; } = [];

    public MultiplePermissionWithGrantedProviders(string[] names)
    {
        Argument.IsNotNull(names);

        foreach (var name in names)
        {
            Result.Add(new PermissionWithGrantedProviders(name, isGranted: false));
        }
    }
}
