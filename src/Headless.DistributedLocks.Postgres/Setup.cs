// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks.Postgres;

[PublicAPI]
public static class SetupPostgresDistributedLocks
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPostgresDistributedLocks(IConfiguration configuration)
        {
            services.Configure<PostgresDistributedLockOptions, PostgresDistributedLockOptionsValidator>(configuration);
            // Bind the shared guardrail knobs (waiter caps, resource-name length, polling cadence) so
            // operators can override the constructor defaults from configuration.
            services.Configure<DistributedLockOptions>(configuration.GetSection("Headless:DistributedLocks"));

            return services._AddPostgresDistributedLocksCore();
        }

        public IServiceCollection AddPostgresDistributedLocks(Action<PostgresDistributedLockOptions> setupAction)
        {
            services.Configure<PostgresDistributedLockOptions, PostgresDistributedLockOptionsValidator>(setupAction);

            return services._AddPostgresDistributedLocksCore();
        }

        public IServiceCollection AddPostgresDistributedLocks(
            Action<PostgresDistributedLockOptions, IServiceProvider> setupAction
        )
        {
            services.Configure<PostgresDistributedLockOptions, PostgresDistributedLockOptionsValidator>(setupAction);

            return services._AddPostgresDistributedLocksCore();
        }

        private IServiceCollection _AddPostgresDistributedLocksCore()
        {
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator());

            // Build the data source once and share it across all three consumers (storage, release
            // signal, fencing) so a connection-string configuration produces a single pool rather than
            // three. The owner wrapper centralizes disposal: an injected DataSource is consumer-owned and
            // never disposed, while a connection-string-built one is owned and disposed on container
            // teardown. Consumers inject the NpgsqlDataSource directly and must not dispose it.
            services.TryAddSingleton<PostgresLockDataSource>();
            services.TryAddSingleton(sp => sp.GetRequiredService<PostgresLockDataSource>().DataSource);

            services.TryAddSingleton<PostgresConnectionScopedLockStorage>();
            services.TryAddSingleton<IConnectionScopedLockStorage>(sp =>
                sp.GetRequiredService<PostgresConnectionScopedLockStorage>()
            );
            services.TryAddSingleton<IReleaseSignal, PostgresReleaseSignal>();
            services.TryAddSingleton<IFencingTokenSource, PostgresFencingTokenSource>();
            // Resolve from IOptions so any configuration binding of DistributedLockOptions (the
            // shared guardrail knobs) flows through to the provider rather than constructor defaults.
            services.TryAddSingleton<DistributedLockOptions>(sp =>
                sp.GetRequiredService<IOptions<DistributedLockOptions>>().Value
            );
            services.TryAddSingleton<ConnectionScopedDistributedLock>(sp => new ConnectionScopedDistributedLock(
                sp.GetRequiredService<IConnectionScopedLockStorage>(),
                sp.GetRequiredService<IReleaseSignal>(),
                sp.GetRequiredService<DistributedLockOptions>(),
                sp.GetRequiredService<ILongIdGenerator>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<ConnectionScopedDistributedLock>>(),
                sp.GetService<IFencingTokenSource>(),
                pollingFallback: sp.GetRequiredService<IOptions<PostgresDistributedLockOptions>>().Value.PollingFallback
            ));
            services.TryAddSingleton<IDistributedLock>(sp =>
                sp.GetRequiredService<ConnectionScopedDistributedLock>()
            );
            services.TryAddSingleton<IDistributedReadWriteLock, ConnectionScopedReadWriteLock>();

            return services;
        }
    }
}
