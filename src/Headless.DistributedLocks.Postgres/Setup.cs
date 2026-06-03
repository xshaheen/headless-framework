// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.DistributedLocks.Postgres;

[PublicAPI]
public static class SetupPostgresDistributedLocks
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPostgresDistributedLocks(IConfiguration configuration)
        {
            services.Configure<PostgresDistributedLockOptions, PostgresDistributedLockOptionsValidator>(configuration);

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
            services.TryAddSingleton<PostgresConnectionScopedLockStorage>();
            services.TryAddSingleton<IConnectionScopedLockStorage>(sp =>
                sp.GetRequiredService<PostgresConnectionScopedLockStorage>()
            );
            services.TryAddSingleton<IReleaseSignal, PostgresReleaseSignal>();
            services.TryAddSingleton<IFencingTokenSource, PostgresFencingTokenSource>();
            services.TryAddSingleton<DistributedLockOptions>();
            services.TryAddSingleton<ConnectionScopedDistributedLockProvider>(sp => new ConnectionScopedDistributedLockProvider(
                sp.GetRequiredService<IConnectionScopedLockStorage>(),
                sp.GetRequiredService<IReleaseSignal>(),
                sp.GetRequiredService<DistributedLockOptions>(),
                sp.GetRequiredService<ILongIdGenerator>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<ConnectionScopedDistributedLockProvider>>(),
                sp.GetService<IFencingTokenSource>(),
                pollingFallback: sp.GetRequiredService<IOptions<PostgresDistributedLockOptions>>().Value.PollingFallback
            ));
            services.TryAddSingleton<IDistributedLockProvider>(sp =>
                sp.GetRequiredService<ConnectionScopedDistributedLockProvider>()
            );
            services.TryAddSingleton<IDistributedReaderWriterLockProvider, ConnectionScopedReaderWriterLockProvider>();

            return services;
        }
    }
}
