// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks.Redis;
using Headless.DistributedLocks.Redis.Scripts;
using Headless.Hosting.Initialization;
using Headless.Messaging;
using Headless.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

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
public static class SetupRedisDistributedLocks
{
    extension(HeadlessDistributedLocksSetupBuilder setup)
    {
        /// <summary>
        /// Adds Redis-backed distributed-lock primitives.
        /// Suitable for distributed multi-instance deployments.
        /// </summary>
        /// <remarks>
        /// Prerequisites:
        /// <list type="bullet">
        ///   <item><see cref="IConnectionMultiplexer"/> must be registered</item>
        ///   <item>Call <c>AddHeadlessDistributedLocks(...)</c> before <c>AddHeadlessMessaging(...)</c>
        ///   when push-based lock release wake-ups are needed</item>
        /// </list>
        /// </remarks>
        public HeadlessDistributedLocksSetupBuilder UseRedis()
        {
            setup.RegisterExtension(new RedisDistributedLocksOptionsExtension());

            return setup;
        }
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

    private static IServiceCollection _AddRedisDistributedLocksCore(
        IServiceCollection services,
        Func<IServiceCollection, IServiceCollection> registerStorage,
        IReadOnlyList<RedisScriptDefinition> scriptDefinitions
    )
    {
        services.TryAddKeyedSingleton(
            RedisDistributedLockServiceKeys.ScriptsLoader,
            (sp, _) =>
                new HeadlessRedisScriptsLoader(
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
    /// keeping repeated Redis provider registration idempotent while letting different script families
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

    private sealed class RedisDistributedLocksOptionsExtension : IDistributedLocksOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            _AddRedisDistributedLocksCore(
                services,
                static s => s.AddDistributedLockCore<RedisDistributedLockStorage>(),
                _MutexScripts
            );
            _AddRedisDistributedLocksCore(
                services,
                static s => s.AddDistributedReadWriteLockCore<RedisDistributedReadWriteLockStorage>(),
                _ReaderWriterScripts
            );
            _AddRedisDistributedLocksCore(
                services,
                static s => s.AddDistributedSemaphoreCore<RedisDistributedSemaphoreStorage>(),
                _SemaphoreScripts
            );
        }
    }
}
