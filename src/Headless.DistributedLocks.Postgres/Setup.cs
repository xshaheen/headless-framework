// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.DistributedLocks.Postgres;

[PublicAPI]
public static class SetupPostgresDistributedLocks
{
    extension(HeadlessDistributedLocksSetupBuilder setup)
    {
        public HeadlessDistributedLocksSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessDistributedLocksSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new PostgresDistributedLocksOptionsExtension(configuration));

            return setup;
        }

        public HeadlessDistributedLocksSetupBuilder UsePostgreSql(Action<PostgresDistributedLockOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgresDistributedLocksOptionsExtension(configure));

            return setup;
        }

        public HeadlessDistributedLocksSetupBuilder UsePostgreSql(
            Action<PostgresDistributedLockOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgresDistributedLocksOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PostgresDistributedLocksOptionsExtension : IDistributedLocksOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<PostgresDistributedLockOptions>? _configure;
        private readonly Action<PostgresDistributedLockOptions, IServiceProvider>? _configureWithServices;

        public PostgresDistributedLocksOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public PostgresDistributedLocksOptionsExtension(Action<PostgresDistributedLockOptions> configure)
        {
            _configure = configure;
        }

        public PostgresDistributedLocksOptionsExtension(
            Action<PostgresDistributedLockOptions, IServiceProvider> configure
        )
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<PostgresDistributedLockOptions, PostgresDistributedLockOptionsValidator>(
                    _configuration
                );
            }
            else if (_configure is not null)
            {
                services.Configure<PostgresDistributedLockOptions, PostgresDistributedLockOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<PostgresDistributedLockOptions, PostgresDistributedLockOptionsValidator>(
                    _configureWithServices
                );
            }

            _AddPostgresDistributedLocksCore(services);
        }
    }

    private static IServiceCollection _AddPostgresDistributedLocksCore(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHeadlessGuidGenerator();

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
            sp.GetRequiredService<IGuidGenerator>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ConnectionScopedDistributedLock>>(),
            sp.GetService<IFencingTokenSource>(),
            pollingFallback: sp.GetRequiredService<IOptions<PostgresDistributedLockOptions>>().Value.PollingFallback
        ));
        services.TryAddSingleton<IDistributedLock>(sp => sp.GetRequiredService<ConnectionScopedDistributedLock>());
        services.TryAddSingleton<IDistributedReadWriteLock, ConnectionScopedReadWriteLock>();

        return services;
    }
}
