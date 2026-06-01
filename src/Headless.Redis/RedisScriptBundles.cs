// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Redis;

/// <summary>Redis script definitions used by Redis cache providers.</summary>
public static class RedisCacheScripts
{
    public static IReadOnlyList<RedisScriptDefinition> Definitions { get; } =
    [
        IncrementWithExpireScriptDefinition.Instance,
        RemoveIfEqualScriptDefinition.Instance,
        ReplaceIfEqualScriptDefinition.Instance,
        SetIfHigherScriptDefinition.Instance,
        SetIfLowerScriptDefinition.Instance,
    ];
}

/// <summary>Redis script definitions used by Redis distributed lock providers.</summary>
public static class RedisDistributedLockScripts
{
    public static IReadOnlyList<RedisScriptDefinition> MutexDefinitions { get; } =
    [
        TryAcquireLockWithFenceScriptDefinition.Instance,
        RemoveIfEqualScriptDefinition.Instance,
        ReplaceIfEqualScriptDefinition.Instance,
    ];

    public static IReadOnlyList<RedisScriptDefinition> SemaphoreDefinitions { get; } =
    [
        TryAcquireSemaphoreWithFenceScriptDefinition.Instance,
        TryExtendSemaphoreScriptDefinition.Instance,
        ValidateSemaphoreScriptDefinition.Instance,
        ReleaseSemaphoreScriptDefinition.Instance,
        GetSemaphoreCountScriptDefinition.Instance,
    ];

    public static IReadOnlyList<RedisScriptDefinition> ReaderWriterDefinitions { get; } =
    [
        TryAcquireReadLockScriptDefinition.Instance,
        TryExtendReadLockScriptDefinition.Instance,
        ReleaseReadLockScriptDefinition.Instance,
        TryAcquireWriteLockScriptDefinition.Instance,
        TryExtendWriteLockScriptDefinition.Instance,
        ReleaseWriteLockScriptDefinition.Instance,
    ];

    public static IReadOnlyList<RedisScriptDefinition> Definitions { get; } =
    [
        .. MutexDefinitions,
        .. SemaphoreDefinitions,
        .. ReaderWriterDefinitions,
    ];
}
