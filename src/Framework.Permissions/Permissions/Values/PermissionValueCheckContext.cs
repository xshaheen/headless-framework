using System.Security.Claims;
using Framework.Kernel.Checks;
using Framework.Permissions.Permissions.Definitions;

namespace Framework.Permissions.Permissions.Values;

public class PermissionValueCheckContext(PermissionDefinition permission, ClaimsPrincipal? principal)
{
    public PermissionDefinition Permission { get; } = Argument.IsNotNull(permission);

    public ClaimsPrincipal? Principal { get; } = principal;
}
