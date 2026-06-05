// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.DistributedLocks.SqlServer;

[PublicAPI]
public static class SetupSqlServerDistributedLocks
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSqlServerDistributedLocks(IConfiguration configuration)
        {
            services.Configure<SqlServerDistributedLockOptions, SqlServerDistributedLockOptionsValidator>(configuration);
            services.Configure<DistributedLockOptions>(configuration.GetSection("Headless:DistributedLocks"));

            return services._AddSqlServerDistributedLocksCore();
        }

        public IServiceCollection AddSqlServerDistributedLocks(Action<SqlServerDistributedLockOptions> setupAction)
        {
            services.Configure<SqlServerDistributedLockOptions, SqlServerDistributedLockOptionsValidator>(setupAction);

            return services._AddSqlServerDistributedLocksCore();
        }

        public IServiceCollection AddSqlServerDistributedLocks(
            Action<SqlServerDistributedLockOptions, IServiceProvider> setupAction
        )
        {
            services.Configure<SqlServerDistributedLockOptions, SqlServerDistributedLockOptionsValidator>(setupAction);

            return services._AddSqlServerDistributedLocksCore();
        }

        private IServiceCollection _AddSqlServerDistributedLocksCore()
        {
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator());
            services.TryAddSingleton<SqlServerConnectionScopedLockStorage>();
            services.TryAddSingleton<IConnectionScopedLockStorage>(sp =>
                sp.GetRequiredService<SqlServerConnectionScopedLockStorage>()
            );
            services.TryAddSingleton<IFencingTokenSource, SqlServerFencingTokenSource>();
            services.AddInitializerHostedService<SqlServerDistributedLocksStorageInitializer>();
            services.TryAddSingleton<DistributedLockOptions>(sp =>
                sp.GetRequiredService<IOptions<DistributedLockOptions>>().Value
            );
            services.TryAddSingleton<ConnectionScopedDistributedLockProvider>(sp => new ConnectionScopedDistributedLockProvider(
                sp.GetRequiredService<IConnectionScopedLockStorage>(),
                // SQL Server blocks contended acquires server-side (BlocksServerSide), so the provider's wait loop and
                // the release signal are unreachable; a no-op signal satisfies the constructor contract.
                new NullReleaseSignal(),
                sp.GetRequiredService<DistributedLockOptions>(),
                sp.GetRequiredService<ILongIdGenerator>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<ConnectionScopedDistributedLockProvider>>(),
                sp.GetRequiredService<IOptions<SqlServerDistributedLockOptions>>().Value.EnableFencing
                    ? sp.GetRequiredService<IFencingTokenSource>()
                    : null
            ));
            services.TryAddSingleton<IDistributedLockProvider>(sp =>
                sp.GetRequiredService<ConnectionScopedDistributedLockProvider>()
            );
            services.TryAddSingleton<IDistributedReaderWriterLockProvider, ConnectionScopedReaderWriterLockProvider>();

            return services;
        }
    }
}
