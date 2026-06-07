// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Core;
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
            services.Configure<SqlServerDistributedLockOptions, SqlServerDistributedLockOptionsValidator>(
                configuration
            );
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
            services.AddHeadlessGuidGenerator();
            services.TryAddSingleton<SqlServerConnectionScopedLockStorage>();
            services.TryAddSingleton<IConnectionScopedLockStorage>(sp =>
                sp.GetRequiredService<SqlServerConnectionScopedLockStorage>()
            );
            services.TryAddSingleton<IFencingTokenSource, SqlServerFencingTokenSource>();
            services.AddInitializerHostedService<SqlServerDistributedLocksStorageInitializer>();
            services.AddSingletonOptionValue<DistributedLockOptions>();

            services.TryAddSingleton(sp => new ConnectionScopedDistributedLock(
                sp.GetRequiredService<IConnectionScopedLockStorage>(),
                // SQL Server blocks contended acquires server-side (BlocksServerSide), so the provider's wait loop and
                // the release signal are unreachable; a no-op signal satisfies the constructor contract.
                new NullReleaseSignal(),
                sp.GetRequiredService<DistributedLockOptions>(),
                sp.GetRequiredService<IGuidGenerator>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<ConnectionScopedDistributedLock>>(),
                sp.GetRequiredService<IOptions<SqlServerDistributedLockOptions>>().Value.EnableFencing
                    ? sp.GetRequiredService<IFencingTokenSource>()
                    : null
            ));

            services.TryAddSingleton<IDistributedLock>(sp => sp.GetRequiredService<ConnectionScopedDistributedLock>());
            services.TryAddSingleton<IDistributedReadWriteLock, ConnectionScopedReadWriteLock>();

            return services;
        }
    }
}
