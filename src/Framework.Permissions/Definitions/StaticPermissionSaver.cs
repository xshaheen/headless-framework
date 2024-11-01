using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Framework.Caching;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Permissions.Entities;
using Framework.Permissions.Models;
using Framework.ResourceLocks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Framework.Permissions.Definitions;

public interface IStaticPermissionSaver
{
    Task SaveAsync();
}

public sealed class StaticPermissionSaver(
    IStaticPermissionDefinitionStore staticStore,
    IPermissionGroupDefinitionRecordRepository permissionGroupRepository,
    IPermissionDefinitionRecordRepository permissionRepository,
    IPermissionDefinitionSerializer permissionSerializer,
    ICache cache,
    IOptions<AbpDistributedCacheOptions> cacheOptions,
    IApplicationInformationAccessor applicationInfoAccessor,
    IResourceLockProvider distributedLock,
    IOptions<PermissionManagementProviderOptions> permissionOptions,
    ICancellationTokenProvider cancellationTokenProvider,
    IEventBus distributedEventBus
) : IStaticPermissionSaver
{
    private readonly PermissionManagementProviderOptions _permissionOptions = permissionOptions.Value;
    private readonly AbpDistributedCacheOptions _cacheOptions = cacheOptions.Value;
    private readonly IEventBus _distributedEventBus = distributedEventBus;

    public async Task SaveAsync()
    {
        await using var applicationLockHandle = await distributedLock.TryAcquireAsync(
            _GetApplicationDistributedLockKey()
        );

        if (applicationLockHandle == null)
        {
            /* Another application instance is already doing it */
            return;
        }

        /* NOTE: This can be further optimized by using 4 cache values for:
         * Groups, permissions, deleted groups and deleted permissions.
         * But the code would be more complex. This is enough for now.
         */

        var cacheKey = _GetApplicationHashCacheKey();
        var cachedHash = await cache.GetStringAsync(cacheKey, cancellationTokenProvider.Token);

        var (permissionGroupRecords, permissionRecords) = await permissionSerializer.Serialize(
            await staticStore.GetGroupsAsync()
        );

        var currentHash = _CalculateHash(
            permissionGroupRecords,
            permissionRecords,
            _permissionOptions.DeletedPermissionGroups,
            _permissionOptions.DeletedPermissions
        );

        if (string.Equals(cachedHash, currentHash, StringComparison.Ordinal))
        {
            return;
        }

        await using (
            var commonLockHandle = await distributedLock.TryAcquireAsync(
                _GetCommonDistributedLockKey(),
                TimeSpan.FromMinutes(5)
            )
        )
        {
            if (commonLockHandle == null)
            {
                /* It will re-try */
                throw new AbpException("Could not acquire distributed lock for saving static permissions!");
            }

            var newOrChangedPermissions = new List<string>();
            using (var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                try
                {
                    var hasChangesInGroups = await _UpdateChangedPermissionGroupsAsync(permissionGroupRecords);
                    var hasChangesInPermissions = await _UpdateChangedPermissionsAsync(
                        permissionRecords,
                        newOrChangedPermissions
                    );

                    if (hasChangesInGroups || hasChangesInPermissions)
                    {
                        await cache.SetStringAsync(
                            _GetCommonStampCacheKey(),
                            Guid.NewGuid().ToString(),
                            new DistributedCacheEntryOptions
                            {
                                SlidingExpiration = TimeSpan.FromDays(
                                    30
                                ) //TODO: Make it configurable?
                                ,
                            },
                            cancellationTokenProvider.Token
                        );
                    }
                }
                catch
                {
                    try
                    {
                        await unitOfWork.RollbackAsync();
                    }
                    catch
                    {
                        /* ignored */
                    }

                    throw;
                }

                await unitOfWork.CompleteAsync();
            }

            if (newOrChangedPermissions.Any())
            {
                await _distributedEventBus.PublishAsync(
                    new DynamicPermissionDefinitionsChangedEto
                    {
                        Permissions = newOrChangedPermissions.Distinct().ToList(),
                    }
                );
            }
        }

        await cache.SetStringAsync(
            cacheKey,
            currentHash,
            new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromDays(
                    30
                ) //TODO: Make it configurable?
                ,
            },
            cancellationTokenProvider.Token
        );
    }

    private async Task<bool> _UpdateChangedPermissionGroupsAsync(
        IEnumerable<PermissionGroupDefinitionRecord> permissionGroupRecords
    )
    {
        var newRecords = new List<PermissionGroupDefinitionRecord>();
        var changedRecords = new List<PermissionGroupDefinitionRecord>();

        var permissionGroupRecordsInDatabase = (await permissionGroupRepository.GetListAsync()).ToDictionary(
            x => x.Name,
            StringComparer.Ordinal
        );

        foreach (var permissionGroupRecord in permissionGroupRecords)
        {
            var permissionGroupRecordInDatabase = permissionGroupRecordsInDatabase.GetOrDefault(
                permissionGroupRecord.Name
            );
            if (permissionGroupRecordInDatabase == null)
            {
                /* New group */
                newRecords.Add(permissionGroupRecord);
                continue;
            }

            if (permissionGroupRecord.HasSameData(permissionGroupRecordInDatabase))
            {
                /* Not changed */
                continue;
            }

            /* Changed */
            permissionGroupRecordInDatabase.Patch(permissionGroupRecord);
            changedRecords.Add(permissionGroupRecordInDatabase);
        }

        /* Deleted */
        var deletedRecords =
            _permissionOptions.DeletedPermissionGroups.Count != 0
                ? permissionGroupRecordsInDatabase
                    .Values.Where(x =>
                        _permissionOptions.DeletedPermissionGroups.Contains(x.Name, StringComparer.Ordinal)
                    )
                    .ToArray()
                : [];

        if (newRecords.Count != 0)
        {
            await permissionGroupRepository.InsertManyAsync(newRecords);
        }

        if (changedRecords.Count != 0)
        {
            await permissionGroupRepository.UpdateManyAsync(changedRecords);
        }

        if (deletedRecords.Length != 0)
        {
            await permissionGroupRepository.DeleteManyAsync(deletedRecords);
        }

        return newRecords.Count != 0 || changedRecords.Count != 0 || deletedRecords.Length != 0;
    }

    private async Task<bool> _UpdateChangedPermissionsAsync(
        IEnumerable<PermissionDefinitionRecord> permissionRecords,
        List<string> newOrChangedPermissions
    )
    {
        var newRecords = new List<PermissionDefinitionRecord>();
        var changedRecords = new List<PermissionDefinitionRecord>();

        var permissionRecordsInDatabase = (await permissionRepository.GetListAsync()).ToDictionary(
            x => x.Name,
            StringComparer.Ordinal
        );

        foreach (var permissionRecord in permissionRecords)
        {
            var permissionRecordInDatabase = permissionRecordsInDatabase.GetOrDefault(permissionRecord.Name);
            if (permissionRecordInDatabase == null)
            {
                /* New permission */
                newRecords.Add(permissionRecord);
                continue;
            }

            if (permissionRecord.HasSameData(permissionRecordInDatabase))
            {
                /* Not changed */
                continue;
            }

            /* Changed */
            permissionRecordInDatabase.Patch(permissionRecord);
            changedRecords.Add(permissionRecordInDatabase);
        }

        /* Deleted */
        var deletedRecords = new List<PermissionDefinitionRecord>();

        if (_permissionOptions.DeletedPermissions.Count != 0)
        {
            deletedRecords.AddRange(
                permissionRecordsInDatabase.Values.Where(x =>
                    _permissionOptions.DeletedPermissions.Contains(x.Name, StringComparer.Ordinal)
                )
            );
        }

        if (_permissionOptions.DeletedPermissionGroups.Count != 0)
        {
            deletedRecords.AddIfNotContains(
                permissionRecordsInDatabase.Values.Where(x =>
                    _permissionOptions.DeletedPermissionGroups.Contains(x.GroupName)
                )
            );
        }

        if (newRecords.Count != 0)
        {
            newOrChangedPermissions.AddRange(newRecords.Select(x => x.Name));
            await permissionRepository.InsertManyAsync(newRecords);
        }

        if (changedRecords.Count != 0)
        {
            newOrChangedPermissions.AddRange(newRecords.Select(x => x.Name));
            await permissionRepository.UpdateManyAsync(changedRecords);
        }

        if (deletedRecords.Count != 0)
        {
            await permissionRepository.DeleteManyAsync(deletedRecords);
        }

        return newRecords.Count != 0 || changedRecords.Count != 0 || deletedRecords.Count != 0;
    }

    private string _GetApplicationDistributedLockKey()
    {
        return $"_{applicationInfoAccessor.ApplicationName}_AbpPermissionUpdateLock";
    }

    private string _GetCommonDistributedLockKey()
    {
        return "_Common_AbpPermissionUpdateLock";
    }

    private string _GetApplicationHashCacheKey()
    {
        return $"{_cacheOptions.KeyPrefix}_{applicationInfoAccessor.ApplicationName}_AbpPermissionsHash";
    }

    private string _GetCommonStampCacheKey()
    {
        return $"{_cacheOptions.KeyPrefix}_AbpInMemoryPermissionCacheStamp";
    }

    private static string _CalculateHash(
        PermissionGroupDefinitionRecord[] permissionGroupRecords,
        PermissionDefinitionRecord[] permissionRecords,
        IEnumerable<string> deletedPermissionGroups,
        IEnumerable<string> deletedPermissions
    )
    {
        var jsonSerializerOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    new AbpIgnorePropertiesModifiers<PermissionGroupDefinitionRecord, Guid>().CreateModifyAction(x =>
                        x.Id
                    ),
                    new AbpIgnorePropertiesModifiers<PermissionDefinitionRecord, Guid>().CreateModifyAction(x => x.Id),
                },
            },
        };

        var stringBuilder = new StringBuilder();

        stringBuilder.Append("PermissionGroupRecords:");
        stringBuilder.AppendLine(JsonSerializer.Serialize(permissionGroupRecords, jsonSerializerOptions));

        stringBuilder.Append("PermissionRecords:");
        stringBuilder.AppendLine(JsonSerializer.Serialize(permissionRecords, jsonSerializerOptions));

        stringBuilder.Append("DeletedPermissionGroups:");
        stringBuilder.AppendLine(deletedPermissionGroups.JoinAsString(","));

        stringBuilder.Append("DeletedPermission:");
        stringBuilder.Append(deletedPermissions.JoinAsString(","));

        return stringBuilder.ToString().ToMd5();
    }
}
