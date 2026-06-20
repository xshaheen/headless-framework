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

/// <summary>
/// Provides extension members on <see cref="HeadlessDistributedLocksSetupBuilder"/> to configure the
/// PostgreSQL advisory-lock distributed-lock provider.
/// </summary>
[PublicAPI]
public static class SetupPostgresDistributedLocks
{
    extension(HeadlessDistributedLocksSetupBuilder setup)
    {
        /// <summary>
        /// Configures the distributed-lock provider to use PostgreSQL advisory locks with the supplied
        /// raw connection string.
        /// </summary>
        /// <param name="connectionString">
        /// A valid Npgsql connection string used to build the provider-owned
        /// <see cref="Npgsql.NpgsqlDataSource"/>. Must not be <see langword="null"/>, empty, or
        /// whitespace.
        /// </param>
        /// <returns>The same <see cref="HeadlessDistributedLocksSetupBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="connectionString"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="connectionString"/> is empty or whitespace.
        /// </exception>
        public HeadlessDistributedLocksSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        /// <summary>
        /// Configures the distributed-lock provider to use PostgreSQL advisory locks, binding
        /// <see cref="PostgresDistributedLockOptions"/> from the supplied <see cref="IConfiguration"/>.
        /// </summary>
        /// <param name="configuration">
        /// The configuration section to bind. Must not be <see langword="null"/>.
        /// </param>
        /// <returns>The same <see cref="HeadlessDistributedLocksSetupBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="configuration"/> is <see langword="null"/>.
        /// </exception>
        public HeadlessDistributedLocksSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new PostgresDistributedLocksOptionsExtension(configuration));

            return setup;
        }

        /// <summary>
        /// Configures the distributed-lock provider to use PostgreSQL advisory locks, applying the
        /// supplied delegate to <see cref="PostgresDistributedLockOptions"/>.
        /// </summary>
        /// <param name="configure">
        /// A delegate that configures the options. Must not be <see langword="null"/>.
        /// </param>
        /// <returns>The same <see cref="HeadlessDistributedLocksSetupBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="configure"/> is <see langword="null"/>.
        /// </exception>
        public HeadlessDistributedLocksSetupBuilder UsePostgreSql(Action<PostgresDistributedLockOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgresDistributedLocksOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Configures the distributed-lock provider to use PostgreSQL advisory locks, applying the
        /// supplied delegate (which also receives the <see cref="IServiceProvider"/>) to
        /// <see cref="PostgresDistributedLockOptions"/>.
        /// </summary>
        /// <param name="configure">
        /// A delegate that configures the options with access to the DI container. Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <returns>The same <see cref="HeadlessDistributedLocksSetupBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="configure"/> is <see langword="null"/>.
        /// </exception>
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
