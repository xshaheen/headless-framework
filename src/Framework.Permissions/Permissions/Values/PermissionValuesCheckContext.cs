using System.Security.Claims;
using Framework.Arguments;
using Framework.Permissions.Permissions.Definitions;

namespace Framework.Permissions.Permissions.Values;

public sealed class PermissionValuesCheckContext(List<PermissionDefinition> permissions, ClaimsPrincipal? principal)
{
    public List<PermissionDefinition> Permissions { get; } = Argument.IsNotNull(permissions);

    public ClaimsPrincipal? Principal { get; } = principal;
}
