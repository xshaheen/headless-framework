using Framework.Arguments;
using Microsoft.AspNetCore.Authorization;

namespace Framework.Permissions.PermissionRequirements;

public sealed class PermissionRequirement(string permissionName) : IAuthorizationRequirement
{
    public string PermissionName { get; } = Argument.IsNotNull(permissionName);

    public override string ToString() => $"PermissionRequirement: {PermissionName}";
}
