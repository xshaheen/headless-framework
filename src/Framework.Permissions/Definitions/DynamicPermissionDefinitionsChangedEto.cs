namespace Framework.Permissions.Definitions;

// [EventName("abp.permission-management.dynamic-permission-definitions-changed")]
public sealed class DynamicPermissionDefinitionsChangedEto
{
    public required List<string> Permissions { get; set; }
}
