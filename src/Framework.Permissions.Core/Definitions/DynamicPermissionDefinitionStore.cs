// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using Framework.Abstractions;
using Framework.Caching;
using Framework.Messaging;
using Framework.Permissions.Entities;
using Framework.Permissions.Events;
using Framework.Permissions.Models;
using Framework.Permissions.Repositories;
using Framework.ResourceLocks;
using Framework.Serializer.Modifiers;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace Framework.Permissions.Definitions;

/// <summary>Store for permission definitions that defined dynamically from an external source like a database.</summary>
public interface IDynamicPermissionDefinitionStore
{
    Task<PermissionDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionDefinition>> GetPermissionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionGroupDefinition>> GetGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Save the application static permissions to the dynamic store.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}

public sealed class DynamicPermissionDefinitionStore(
    IPermissionDefinitionRecordRepository repository,
    IStaticPermissionDefinitionStore staticStore,
    IPermissionDefinitionSerializer serializer,
    ICache distributedCache,
    IResourceLockProvider resourceLockProvider,
    IDistributedMessagePublisher messagePublisher,
    IGuidGenerator guidGenerator,
    IApplicationInformationAccessor application,
    IOptions<PermissionManagementOptions> optionsAccessor,
    IOptions<PermissionManagementProvidersOptions> providersAccessor,
    TimeProvider timeProvider
) : IDynamicPermissionDefinitionStore, IDisposable
{
    private readonly PermissionManagementOptions _options = optionsAccessor.Value;
    private readonly PermissionManagementProvidersOptions _providers = providersAccessor.Value;

    /// <summary>
    /// A lock key for the application permissions update to allow only one instance to try
    /// to save the changes at a time.
    /// </summary>
    private readonly string _appSaveLockKey = $"{application.ApplicationName}_PermissionsUpdateLock";

    /// <summary>A hash of the application permissions to check if there are changes and need to save them.</summary>
    private readonly string _appSavePermissionsHashCacheKey = $"{application.ApplicationName}_PermissionsHash";

    #region Get Methods

    public async Task<PermissionDefinition?> GetOrDefaultAsync(
        string name,
        CancellationToken cancellationToken = default
    )
    {
        if (!_options.IsDynamicPermissionStoreEnabled)
        {
            return null;
        }

        using (await _syncSemaphore.LockAsync(cancellationToken))
        {
            await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken);
            return _permissionMemoryCache.GetOrDefault(name);
        }
    }

    public async Task<IReadOnlyList<PermissionDefinition>> GetPermissionsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!_options.IsDynamicPermissionStoreEnabled)
        {
            return [];
        }

        using (await _syncSemaphore.LockAsync(cancellationToken))
        {
            await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken);
            return _permissionMemoryCache.Values.ToImmutableList();
        }
    }

    public async Task<IReadOnlyList<PermissionGroupDefinition>> GetGroupsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!_options.IsDynamicPermissionStoreEnabled)
        {
            return [];
        }

        using (await _syncSemaphore.LockAsync(cancellationToken))
        {
            await _EnsureMemoryCacheIsUptoDateAsync(cancellationToken);
            return _groupMemoryCache.Values.ToImmutableList();
        }
    }

    #endregion

    #region Get Helpers

    private string? _cacheStamp;
    private DateTimeOffset? _lastCheckTime;
    private readonly SemaphoreSlim _syncSemaphore = new(1, 1);
    private readonly Dictionary<string, PermissionGroupDefinition> _groupMemoryCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PermissionDefinition> _permissionMemoryCache = new(StringComparer.Ordinal);

    private async Task _EnsureMemoryCacheIsUptoDateAsync(CancellationToken cancellationToken)
    {
        if (!_IsUpdateMemoryCacheRequired())
        {
            return; // Get the latest feature with a small delay for optimization
        }

        var cacheStamp = await _GetOrSetDistributedCacheStampAsync(cancellationToken);

        if (string.Equals(cacheStamp, _cacheStamp, StringComparison.Ordinal))
        {
            _lastCheckTime = timeProvider.GetUtcNow();

            return;
        }

        await _UpdateInMemoryStoreCacheAsync(cancellationToken);
        _cacheStamp = cacheStamp;
        _lastCheckTime = timeProvider.GetUtcNow();
    }

    private async Task<string> _GetOrSetDistributedCacheStampAsync(CancellationToken cancellationToken)
    {
        var cacheKey = _options.CommonPermissionsUpdatedStampCacheKey;
        var cachedStamp = await distributedCache.GetAsync<string>(cacheKey, cancellationToken);

        if (!cachedStamp.IsNull)
        {
            return cachedStamp.Value;
        }

        await using var commonLockHandle =
            await resourceLockProvider.TryAcquireAsync(
                resource: _options.CrossApplicationsCommonLockKey,
                timeUntilExpires: _options.CrossApplicationsCommonLockExpiration,
                acquireTimeout: _options.CrossApplicationsCommonLockAcquireTimeout,
                cancellationToken: cancellationToken
            )
            ?? throw new InvalidOperationException(
                "Could not acquire distributed lock for permission definition common stamp check!"
            ); // This request will fail

        cancellationToken.ThrowIfCancellationRequested();

        cachedStamp = await distributedCache.GetAsync<string>(cacheKey, cancellationToken);

        if (!cachedStamp.IsNull)
        {
            return cachedStamp.Value;
        }

        return await _ChangeCommonStampAsync(cancellationToken);
    }

    private async Task _UpdateInMemoryStoreCacheAsync(CancellationToken cancellationToken)
    {
        var permissionGroupRecords = await repository.GetGroupsListAsync(cancellationToken);
        var permissionRecords = await repository.GetPermissionsListAsync(cancellationToken);

        _groupMemoryCache.Clear();
        _permissionMemoryCache.Clear();

        var context = new PermissionDefinitionContext();

        foreach (var permissionGroupRecord in permissionGroupRecords)
        {
            var permissionGroup = context.AddGroup(permissionGroupRecord.Name, permissionGroupRecord.DisplayName);

            _groupMemoryCache[permissionGroup.Name] = permissionGroup;

            foreach (var property in permissionGroupRecord.ExtraProperties)
            {
                permissionGroup[property.Key] = property.Value;
            }

            var permissionRecordsInThisGroup = permissionRecords.Where(p =>
                string.Equals(p.GroupName, permissionGroup.Name, StringComparison.Ordinal)
            );

            foreach (var permissionRecord in permissionRecordsInThisGroup.Where(x => x.ParentName == null))
            {
                _UpdateInMemoryStoreCacheAddFeatureRecursively(permissionGroup, permissionRecord, permissionRecords);
            }
        }
    }

    private void _UpdateInMemoryStoreCacheAddFeatureRecursively(
        ICanAddChildPermission permissionContainer,
        PermissionDefinitionRecord permissionRecord,
        List<PermissionDefinitionRecord> allPermissionRecords
    )
    {
        var permission = permissionContainer.AddChild(
            permissionRecord.Name,
            permissionRecord.DisplayName,
            permissionRecord.IsEnabled
        );

        _permissionMemoryCache[permission.Name] = permission;

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
            _UpdateInMemoryStoreCacheAddFeatureRecursively(permission, subPermission, allPermissionRecords);
        }
    }

    private bool _IsUpdateMemoryCacheRequired()
    {
        if (!_lastCheckTime.HasValue)
        {
            return true;
        }

        var elapsedSinceLastCheck = timeProvider.GetUtcNow().Subtract(_lastCheckTime.Value);

        return elapsedSinceLastCheck > _options.DynamicDefinitionsMemoryCacheExpiration;
    }

    public void Dispose()
    {
        _syncSemaphore.Dispose();
    }

    #endregion

    #region Save Methods

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await using var appResourceLock = await resourceLockProvider.TryAcquireAsync(
            _appSaveLockKey,
            timeUntilExpires: _options.ApplicationSaveLockExpiration,
            acquireTimeout: _options.ApplicationSaveLockAcquireTimeout,
            cancellationToken: cancellationToken
        );

        if (appResourceLock is null)
        {
            return; // Another application instance is already doing it
        }

        cancellationToken.ThrowIfCancellationRequested();

        // NOTE: This can be further optimized by using 4 cache values for:
        // Groups, features, deleted groups and deleted features.
        // But the code would be more complex.
        // This is enough for now.

        var cachedHash = await distributedCache.GetAsync<string>(_appSavePermissionsHashCacheKey, cancellationToken);
        var groups = await staticStore.GetGroupsAsync(cancellationToken);
        var (permissionGroupRecords, permissionRecords) = serializer.Serialize(groups);

        var currentHash = _CalculateHash(
            permissionGroupRecords,
            permissionRecords,
            _providers.DeletedPermissionGroups,
            _providers.DeletedPermissions
        );

        if (string.Equals(cachedHash.Value, currentHash, StringComparison.Ordinal))
        {
            return; // No changes
        }

        await using var commonLockHandle =
            await resourceLockProvider.TryAcquireAsync(
                resource: _options.CrossApplicationsCommonLockKey,
                timeUntilExpires: _options.CrossApplicationsCommonLockExpiration,
                acquireTimeout: _options.CrossApplicationsCommonLockAcquireTimeout,
                cancellationToken: cancellationToken
            )
            ?? throw new InvalidOperationException("Could not acquire distributed lock for saving static permissions!"); // It will re-try

        var (newGroups, updatedGroups, deletedGroups) = await _UpdateChangedPermissionGroupsAsync(
            permissionGroupRecords,
            cancellationToken
        );

        var (newPermissions, updatedPermissions, deletedPermissions) = await _UpdateChangedPermissionsAsync(
            permissionRecords,
            cancellationToken
        );

        var hasChangesInGroups = newGroups.Count != 0 || updatedGroups.Count != 0 || deletedGroups.Count != 0;
        var hasChangesInPermissions =
            newPermissions.Count != 0 || updatedPermissions.Count != 0 || deletedPermissions.Count != 0;

        if (hasChangesInGroups || hasChangesInPermissions)
        {
            await repository.SaveAsync(
                newGroups,
                updatedGroups,
                deletedGroups,
                newPermissions,
                updatedPermissions,
                deletedPermissions,
                cancellationToken
            );

            await _ChangeCommonStampAsync(cancellationToken);
        }

        if (newPermissions.Count != 0 || updatedPermissions.Count != 0)
        {
            var message = new DynamicPermissionDefinitionsChanged
            {
                UniqueId = guidGenerator.Create().ToString(),
                Timestamp = timeProvider.GetUtcNow(),
                Permissions = [.. newPermissions.Select(x => x.Name), .. updatedPermissions.Select(x => x.Name)],
            };

            await messagePublisher.PublishAsync(message, cancellationToken);
        }

        await distributedCache.UpsertAsync(
            _appSavePermissionsHashCacheKey,
            currentHash,
            _options.PermissionsHashCacheExpiration,
            cancellationToken
        );
    }

    #endregion

    #region Save Helpers

    private async Task<(
        List<PermissionGroupDefinitionRecord> NewRecords,
        List<PermissionGroupDefinitionRecord> ChangedRecords,
        List<PermissionGroupDefinitionRecord> DeletedRecords
    )> _UpdateChangedPermissionGroupsAsync(
        IEnumerable<PermissionGroupDefinitionRecord> permissionGroupRecords,
        CancellationToken cancellationToken
    )
    {
        var dbRecords = await repository.GetGroupsListAsync(cancellationToken);
        var dbRecordsMap = dbRecords.ToDictionary(x => x.Name, StringComparer.Ordinal);

        var newRecords = new List<PermissionGroupDefinitionRecord>();
        var changedRecords = new List<PermissionGroupDefinitionRecord>();
        var deletedRecords = new List<PermissionGroupDefinitionRecord>();

        foreach (var permissionGroupRecord in permissionGroupRecords)
        {
            var dbRecord = dbRecordsMap.GetOrDefault(permissionGroupRecord.Name);

            if (dbRecord is null) // New
            {
                newRecords.Add(permissionGroupRecord);

                continue;
            }

            if (permissionGroupRecord.HasSameData(dbRecord)) // Not changed
            {
                continue;
            }

            dbRecord.Patch(permissionGroupRecord); // Changed
            changedRecords.Add(dbRecord);
        }

        // Handle deleted records
        if (_providers.DeletedPermissionGroups.Count != 0)
        {
            deletedRecords.AddRange(dbRecords.Where(x => _providers.DeletedPermissionGroups.Contains(x.Name)));
        }

        return (newRecords, changedRecords, deletedRecords);
    }

    private static string _CalculateHash(
        IReadOnlyCollection<PermissionGroupDefinitionRecord> permissionGroupRecords,
        IReadOnlyCollection<PermissionDefinitionRecord> permissionRecords,
        IReadOnlyCollection<string> deletedPermissionGroups,
        IReadOnlyCollection<string> deletedPermissions
    )
    {
        var stringBuilder = new StringBuilder();

        stringBuilder.Append("PermissionGroupRecords:");
        stringBuilder.AppendLine(JsonSerializer.Serialize(permissionGroupRecords, _JsonSerializerOptions));

        stringBuilder.Append("PermissionRecords:");
        stringBuilder.AppendLine(JsonSerializer.Serialize(permissionRecords, _JsonSerializerOptions));

        stringBuilder.Append("DeletedPermissionGroups:");
        stringBuilder.AppendLine(deletedPermissionGroups.JoinAsString(","));

        stringBuilder.Append("DeletedPermission:");
        stringBuilder.Append(deletedPermissions.JoinAsString(","));

        return stringBuilder.ToString().ToMd5();
    }

    private async Task<(
        List<PermissionDefinitionRecord> NewRecords,
        List<PermissionDefinitionRecord> ChangedRecords,
        List<PermissionDefinitionRecord> DeletedRecords
    )> _UpdateChangedPermissionsAsync(
        IEnumerable<PermissionDefinitionRecord> permissionRecords,
        CancellationToken cancellationToken = default
    )
    {
        var newRecords = new List<PermissionDefinitionRecord>();
        var changedRecords = new List<PermissionDefinitionRecord>();
        var deletedRecords = new List<PermissionDefinitionRecord>();

        var dbRecords = await repository.GetPermissionsListAsync(cancellationToken);
        var dbRecordsMap = dbRecords.ToDictionary(x => x.Name, StringComparer.Ordinal);

        // Handle new and changed records
        foreach (var permissionRecord in permissionRecords)
        {
            var dbRecord = dbRecordsMap.GetOrDefault(permissionRecord.Name);

            if (dbRecord is null) // New
            {
                newRecords.Add(permissionRecord);

                continue;
            }

            if (permissionRecord.HasSameData(dbRecord)) // Not changed
            {
                continue;
            }

            dbRecord.Patch(permissionRecord); // Changed
            changedRecords.Add(dbRecord);
        }

        // Handle deleted records
        if (_providers.DeletedPermissions.Count != 0)
        {
            deletedRecords.AddRange(dbRecordsMap.Values.Where(x => _providers.DeletedPermissions.Contains(x.Name)));
        }

        if (_providers.DeletedPermissionGroups.Count != 0)
        {
            deletedRecords.AddIfNotContains(
                dbRecordsMap.Values.Where(x => _providers.DeletedPermissionGroups.Contains(x.GroupName))
            );
        }

        return (newRecords, changedRecords, deletedRecords);
    }

    private static readonly JsonSerializerOptions _JsonSerializerOptions = _CreateHashJsonSerializerOptions();

    private static JsonSerializerOptions _CreateHashJsonSerializerOptions()
    {
        return new()
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    JsonPropertiesModifiers<PermissionGroupDefinitionRecord>.CreateIgnorePropertyModifyAction(x =>
                        x.Id
                    ),
                    JsonPropertiesModifiers<PermissionDefinitionRecord>.CreateIgnorePropertyModifyAction(x => x.Id),
                },
            },
        };
    }

    #endregion

    #region Helpers

    private async Task<string> _ChangeCommonStampAsync(CancellationToken cancellationToken)
    {
        var stamp = guidGenerator.Create().ToString("N");

        await distributedCache.UpsertAsync(
            _options.CommonPermissionsUpdatedStampCacheKey,
            stamp,
            _options.CommonPermissionsUpdatedStampCacheExpiration,
            cancellationToken
        );

        return stamp;
    }

    #endregion
}
