// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.DistributedLocks.SqlServer;

[PublicAPI]
public static class SetupSqlServerDistributedLocks
{
    extension(HeadlessDistributedLocksSetupBuilder setup)
    {
        public HeadlessDistributedLocksSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessDistributedLocksSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new SqlServerDistributedLocksOptionsExtension(configuration));

            return setup;
        }

        public HeadlessDistributedLocksSetupBuilder UseSqlServer(Action<SqlServerDistributedLockOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerDistributedLocksOptionsExtension(configure));

            return setup;
        }

        public HeadlessDistributedLocksSetupBuilder UseSqlServer(
            Action<SqlServerDistributedLockOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerDistributedLocksOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class SqlServerDistributedLocksOptionsExtension : IDistributedLocksOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<SqlServerDistributedLockOptions>? _configure;
        private readonly Action<SqlServerDistributedLockOptions, IServiceProvider>? _configureWithServices;

        public SqlServerDistributedLocksOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public SqlServerDistributedLocksOptionsExtension(Action<SqlServerDistributedLockOptions> configure)
        {
            _configure = configure;
        }

        public SqlServerDistributedLocksOptionsExtension(
            Action<SqlServerDistributedLockOptions, IServiceProvider> configure
        )
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<SqlServerDistributedLockOptions, SqlServerDistributedLockOptionsValidator>(
                    _configuration
                );
            }
            else if (_configure is not null)
            {
                services.Configure<SqlServerDistributedLockOptions, SqlServerDistributedLockOptionsValidator>(
                    _configure
                );
            }
            else
            {
                services.Configure<SqlServerDistributedLockOptions, SqlServerDistributedLockOptionsValidator>(
                    _configureWithServices
                );
            }

            _AddSqlServerDistributedLocksCore(services);
        }
    }

    private static IServiceCollection _AddSqlServerDistributedLocksCore(IServiceCollection services)
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
