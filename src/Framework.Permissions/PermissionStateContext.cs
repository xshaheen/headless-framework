using Framework.Permissions.Permissions.Definitions;

namespace Framework.Permissions;

public class PermissionStateContext
{
    public IServiceProvider ServiceProvider { get; set; } = default!;

    public PermissionDefinition Permission { get; set; } = default!;
}
