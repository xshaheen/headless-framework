// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Messaging;
using Headless.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis;

/// <summary>
/// Extension methods for registering Redis-backed resource locks.
/// Suitable for distributed multi-instance deployments.
/// </summary>
/// <remarks>
/// Requires <see cref="IConnectionMultiplexer"/> to be registered in the service collection.
/// Messaging is optional; when an <see cref="IOutboxBus"/> registration exists before lock setup,
/// release notifications use push wake-ups. Otherwise, waiters fall back to polling backoff.
/// </remarks>
[PublicAPI]
public static class SetupRedisDistributedLock
{
    extension(IServiceCollection services)
    {
        #region Redis Distributed Lock

        /// <summary>
        /// Adds Redis-backed resource lock provider.
        /// Suitable for distributed multi-instance deployments.
        /// </summary>
        /// <remarks>
        /// Prerequisites:
        /// <list type="bullet">
        ///   <item><see cref="IConnectionMultiplexer"/> must be registered</item>
        ///   <item>Register messaging before this method when push-based lock release wake-ups are needed</item>
        /// </list>
        /// </remarks>
        public IServiceCollection AddRedisDistributedLock(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
        {
            return services._AddRedisDistributedCore(
                s => s.AddDistributedLock<RedisDistributedLockStorage>(optionSetupAction),
                _MutexScripts
            );
        }

        /// <summary>Adds Redis-backed resource lock provider.</summary>
        /// <remarks>
        /// <paramref name="optionSetupAction"/> is optional; when omitted, <see cref="DistributedLockOptions"/>
        /// keeps its defaults.
        /// </remarks>
        public IServiceCollection AddRedisDistributedLock(Action<DistributedLockOptions>? optionSetupAction = null)
        {
            return services._AddRedisDistributedCore(
                s => s.AddDistributedLock<RedisDistributedLockStorage>(optionSetupAction ?? (static _ => { })),
                _MutexScripts
            );
        }

        /// <summary>Adds Redis-backed resource lock provider.</summary>
        public IServiceCollection AddRedisDistributedLock(IConfiguration config)
        {
            return services._AddRedisDistributedCore(
                s => s.AddDistributedLock<RedisDistributedLockStorage>(config),
                _MutexScripts
            );
        }

        #endregion

        #region Redis Distributed Semaphore

        /// <summary>Adds Redis-backed distributed semaphore provider.</summary>
        public IServiceCollection AddRedisDistributedSemaphore(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
        {
            return services._AddRedisDistributedCore(
                s => s.AddDistributedSemaphore<RedisDistributedSemaphoreStorage>(optionSetupAction),
                _SemaphoreScripts
            );
        }

        /// <summary>Adds Redis-backed distributed semaphore provider.</summary>
        public IServiceCollection AddRedisDistributedSemaphore(Action<DistributedLockOptions> optionSetupAction)
        {
            return services._AddRedisDistributedCore(
                s => s.AddDistributedSemaphore<RedisDistributedSemaphoreStorage>(optionSetupAction),
                _SemaphoreScripts
            );
        }

        /// <summary>Adds Redis-backed distributed semaphore provider.</summary>
        public IServiceCollection AddRedisDistributedSemaphore(IConfiguration config)
        {
            return services._AddRedisDistributedCore(
                s => s.AddDistributedSemaphore<RedisDistributedSemaphoreStorage>(config),
                _SemaphoreScripts
            );
        }

        #endregion

        #region Redis Distributed Reader-Writer Lock

        /// <summary>Adds Redis-backed distributed reader-writer lock provider.</summary>
        public IServiceCollection AddRedisDistributedReadWriteLock(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
        {
            return services._AddRedisDistributedCore(
                s => s.AddDistributedReadWriteLock<RedisDistributedReadWriteLockStorage>(optionSetupAction),
                _ReaderWriterScripts
            );
        }

        /// <summary>Adds Redis-backed distributed reader-writer lock provider.</summary>
        public IServiceCollection AddRedisDistributedReadWriteLock(Action<DistributedLockOptions> optionSetupAction)
        {
            return services._AddRedisDistributedCore(
                s => s.AddDistributedReadWriteLock<RedisDistributedReadWriteLockStorage>(optionSetupAction),
                _ReaderWriterScripts
            );
        }

        /// <summary>Adds Redis-backed distributed reader-writer lock provider.</summary>
        public IServiceCollection AddRedisDistributedReadWriteLock(IConfiguration config)
        {
            return services._AddRedisDistributedCore(
                s => s.AddDistributedReadWriteLock<RedisDistributedReadWriteLockStorage>(config),
                _ReaderWriterScripts
            );
        }

        #endregion
    }

    private static readonly IReadOnlyList<RedisScriptDefinition> _MutexScripts =
    [
        TryAcquireLockWithFenceScriptDefinition.Instance,
        RemoveIfEqualScriptDefinition.Instance,
        ReplaceIfEqualScriptDefinition.Instance,
    ];

    private static readonly IReadOnlyList<RedisScriptDefinition> _ReaderWriterScripts =
    [
        TryAcquireReadLockScriptDefinition.Instance,
        TryExtendReadLockScriptDefinition.Instance,
        ReleaseReadLockScriptDefinition.Instance,
        TryAcquireWriteLockScriptDefinition.Instance,
        TryExtendWriteLockScriptDefinition.Instance,
        ReleaseWriteLockScriptDefinition.Instance,
    ];

    private static readonly IReadOnlyList<RedisScriptDefinition> _SemaphoreScripts =
    [
        TryAcquireSemaphoreWithFenceScriptDefinition.Instance,
        TryExtendSemaphoreScriptDefinition.Instance,
        ValidateSemaphoreScriptDefinition.Instance,
        ReleaseSemaphoreScriptDefinition.Instance,
        GetSemaphoreCountScriptDefinition.Instance,
    ];

    private static IServiceCollection _AddRedisDistributedCore(
        this IServiceCollection services,
        Func<IServiceCollection, IServiceCollection> registerStorage,
        IReadOnlyList<RedisScriptDefinition> scriptDefinitions
    )
    {
        services.TryAddKeyedSingleton(
            RedisDistributedLockServiceKeys.ScriptsLoader,
            (sp, _) => new HeadlessRedisScriptsLoader(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetService<TimeProvider>(),
                sp.GetService<ILogger<HeadlessRedisScriptsLoader>>()
            )
        );

        _AddScriptsInitializer(services, scriptDefinitions);

        return registerStorage(services);
    }

    /// <summary>
    /// Registers a single <see cref="RedisScriptsInitializer"/> instance carrying
    /// <paramref name="definitions"/>, forwarded as both <see cref="IInitializer"/> and
    /// <see cref="IHostedService"/> so the host runs <c>StartingAsync</c> and dependents awaiting
    /// <c>WaitForInitializationAsync</c> observe the SAME instance's completion promise.
    /// </summary>
    /// <remarks>
    /// Each lock family contributes a distinct instance of the shared type, so it cannot be
    /// deduplicated by <c>TryAddEnumerable</c> (which keys on implementation type). The lazy holder
    /// is recorded as a marker descriptor and reused if the same definition list is registered again,
    /// keeping repeated <c>AddRedisDistributed*</c> calls idempotent while letting different families
    /// each register once. The lazy build defers loader resolution to first start.
    /// </remarks>
    private static void _AddScriptsInitializer(
        IServiceCollection services,
        IReadOnlyList<RedisScriptDefinition> definitions
    )
    {
        // Reuse the holder for this definition list if the same family was already added so both
        // forwarders resolve to one shared instance and repeated registrations stay idempotent.
        var existingHolder = services
            .Select(d => d.ImplementationInstance)
            .OfType<RedisScriptsInitializerHolder>()
            .FirstOrDefault(holder => ReferenceEquals(holder.Definitions, definitions));

        if (existingHolder is not null)
        {
            return;
        }

        var holder = new RedisScriptsInitializerHolder(definitions);

        // The holder is registered under its own type purely so the dedupe scan above can find it;
        // it is never resolved as a service dependency.
        services.AddSingleton(holder);
        services.AddSingleton<IInitializer>(sp => holder.Get(sp));
        services.AddSingleton<IHostedService>(sp => holder.Get(sp));
    }

    /// <summary>
    /// Lazily materializes a single <see cref="RedisScriptsInitializer"/> for a definition list so
    /// the two interface forwarders share one instance (and therefore one completion promise).
    /// </summary>
    private sealed class RedisScriptsInitializerHolder(IReadOnlyList<RedisScriptDefinition> definitions)
    {
        private readonly Lock _gate = new();
        private RedisScriptsInitializer? _instance;

        public IReadOnlyList<RedisScriptDefinition> Definitions { get; } = definitions;

        public RedisScriptsInitializer Get(IServiceProvider sp)
        {
            if (_instance is { } existing)
            {
                return existing;
            }

            lock (_gate)
            {
                if (_instance is { } current)
                {
                    return current;
                }

                var loader = sp.GetRequiredKeyedService<HeadlessRedisScriptsLoader>(
                    RedisDistributedLockServiceKeys.ScriptsLoader
                );

                _instance = new RedisScriptsInitializer(loader, Definitions);

                return _instance;
            }
        }
    }
}
