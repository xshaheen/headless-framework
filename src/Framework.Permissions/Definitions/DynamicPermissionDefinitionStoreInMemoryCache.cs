using Framework.Permissions.Entities;

namespace Framework.Permissions.Definitions;

public interface IDynamicPermissionDefinitionStoreInMemoryCache
{
    string? CacheStamp { get; set; }

    SemaphoreSlim SyncSemaphore { get; }

    DateTime? LastCheckTime { get; set; }

    Task FillAsync(
        List<PermissionGroupDefinitionRecord> permissionGroupRecords,
        List<PermissionDefinitionRecord> permissionRecords
    );

    PermissionDefinition? GetPermissionOrDefault(string name);

    IReadOnlyList<PermissionDefinition> GetPermissions();

    IReadOnlyList<PermissionGroupDefinition> GetGroups();
}

public sealed class DynamicPermissionDefinitionStoreInMemoryCache : IDynamicPermissionDefinitionStoreInMemoryCache
{
    private readonly Dictionary<string, PermissionGroupDefinition> _permissionGroupDefinitions =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, PermissionDefinition> _permissionDefinitions = new(StringComparer.Ordinal);

    public string? CacheStamp { get; set; }
    public SemaphoreSlim SyncSemaphore { get; } = new(1, 1);

    public DateTime? LastCheckTime { get; set; }

    public Task FillAsync(
        List<PermissionGroupDefinitionRecord> permissionGroupRecords,
        List<PermissionDefinitionRecord> permissionRecords
    )
    {
        _permissionGroupDefinitions.Clear();
        _permissionDefinitions.Clear();

        var context = new PermissionDefinitionContext();

        foreach (var permissionGroupRecord in permissionGroupRecords)
        {
            var permissionGroup = context.AddGroup(permissionGroupRecord.Name, permissionGroupRecord.DisplayName);

            _permissionGroupDefinitions[permissionGroup.Name] = permissionGroup;

            foreach (var property in permissionGroupRecord.ExtraProperties)
            {
                permissionGroup[property.Key] = property.Value;
            }

            var permissionRecordsInThisGroup = permissionRecords.Where(p =>
                string.Equals(p.GroupName, permissionGroup.Name, StringComparison.Ordinal)
            );

            foreach (var permissionRecord in permissionRecordsInThisGroup.Where(x => x.ParentName == null))
            {
                _AddPermissionRecursively(permissionGroup, permissionRecord, permissionRecords);
            }
        }

        return Task.CompletedTask;
    }

    public PermissionDefinition? GetPermissionOrDefault(string name)
    {
        return _permissionDefinitions.GetOrDefault(name);
    }

    public IReadOnlyList<PermissionDefinition> GetPermissions()
    {
        return _permissionDefinitions.Values.ToList();
    }

    public IReadOnlyList<PermissionGroupDefinition> GetGroups()
    {
        return _permissionGroupDefinitions.Values.ToList();
    }

    private void _AddPermissionRecursively(
        ICanAddChildPermission permissionContainer,
        PermissionDefinitionRecord permissionRecord,
        List<PermissionDefinitionRecord> allPermissionRecords
    )
    {
        var permission = permissionContainer.AddPermission(
            permissionRecord.Name,
            permissionRecord.DisplayName,
            permissionRecord.IsEnabled
        );

        _permissionDefinitions[permission.Name] = permission;

        if (!permissionRecord.Providers.IsNullOrWhiteSpace())
        {
            permission.Providers.AddRange(permissionRecord.Providers.Split(','));
        }

        foreach (var property in permissionRecord.ExtraProperties)
        {
            permission[property.Key] = property.Value;
        }

        foreach (
            var subPermission in allPermissionRecords.Where(p =>
                string.Equals(p.ParentName, permissionRecord.Name, StringComparison.Ordinal)
            )
        )
        {
            _AddPermissionRecursively(permission, subPermission, allPermissionRecords);
        }
    }
}
