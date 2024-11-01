using Framework.Kernel.Checks;
using Framework.Permissions.Models;

namespace Framework.Permissions.PermissionManagement;

public sealed class MultiplePermissionWithGrantedProviders
{
    public List<PermissionWithGrantedProviders> Result { get; }

    public MultiplePermissionWithGrantedProviders()
    {
        Result = [];
    }

    public MultiplePermissionWithGrantedProviders(string[] names)
    {
        Argument.IsNotNull(names);

        Result = [];

        foreach (var name in names)
        {
            Result.Add(new PermissionWithGrantedProviders(name, isGranted: false));
        }
    }
}
