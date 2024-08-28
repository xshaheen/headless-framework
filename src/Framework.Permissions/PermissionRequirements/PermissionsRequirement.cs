using Framework.Arguments;
using Microsoft.AspNetCore.Authorization;

namespace Framework.Permissions.PermissionRequirements;

public sealed class PermissionsRequirement(string[] permissionNames, bool requiresAll) : IAuthorizationRequirement
{
    public string[] PermissionNames { get; } = Argument.IsNotNull(permissionNames);

    public bool RequiresAll { get; } = requiresAll;

    public override string ToString()
    {
        return $"PermissionsRequirement: {string.Join(", ", PermissionNames)}";
    }
}
