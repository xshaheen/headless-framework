namespace Framework.Permissions.Permissions.Definitions;

public interface ICanAddChildPermission
{
    PermissionDefinition AddPermission(string name, string? displayName = null, bool isEnabled = true);
}
