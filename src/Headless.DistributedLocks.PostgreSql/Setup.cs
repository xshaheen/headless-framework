// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.DistributedLocks.PostgreSql;
using Headless.Hosting;
using Headless.Hosting.Schema;
using Headless.Sql;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.DistributedLocks;

/// <summary>
/// Provides extension members on <see cref="HeadlessDistributedLocksSetupBuilder"/> to configure the
/// PostgreSQL advisory-lock distributed-lock provider.
/// </summary>
[PublicAPI]
public static class SetupDistributedLocksPostgreSql
{
    extension(HeadlessDistributedLocksSetupBuilder setup)
    {
        /// <summary>
        /// Configures PostgreSQL storage on the application's connection: the container's
        /// <see cref="NpgsqlDataSource"/> when one is registered, otherwise the connection registered by
        /// <c>AddPostgreSqlSql</c>, so locks share the pool every other feature on that database uses.
        /// </summary>
        /// <returns>The setup builder for chaining.</returns>
        /// <remarks>
        /// <para>
        /// Neither path builds a provider-owned pool or applies <see cref="PostgreSqlDistributedLockOptions.KeepAlive"/>:
        /// set <c>Keepalive</c> on the application's connection string or data source to have a silently dropped lock
        /// holder surface faster. The active connection monitor detects a dead holder either way.
        /// </para>
        /// <para>
        /// A registered data source that sets <c>Multiplexing</c> is skipped in favour of the <c>AddPostgreSqlSql</c>
        /// connection: multiplexing binds no physical connection, so it cannot hold a session advisory lock. Validation
        /// fails at startup, naming the setting, when the source the lock ends up with multiplexes.
        /// </para>
        /// <para>
        /// Options resolution throws <see cref="InvalidOperationException"/> when no <see cref="NpgsqlDataSource"/> is
        /// registered and <c>AddPostgreSqlSql</c> was not called or registered another provider's connection.
        /// </para>
        /// </remarks>
        public HeadlessDistributedLocksSetupBuilder UsePostgreSql()
        {
            return setup.UsePostgreSql(
                static (options, services) =>
                {
                    // The application's own data source wins, so locks draw from the pool (and the settings) the
                    // application already configured instead of opening a second one against the same server. A
                    // multiplexing one cannot hold a session lock, so the shared connection string serves instead when
                    // one is registered; with none, the multiplexing source stays and validation names the setting.
                    if (services.GetService<NpgsqlDataSource>() is { } dataSource)
                    {
                        if (
                            !PostgresDataSourceFactory.IsMultiplexing(dataSource.ConnectionString)
                            || services.GetService<ISqlConnectionFactory>() is not NpgsqlConnectionFactory
                        )
                        {
                            options.DataSource = dataSource;

                            return;
                        }
                    }

                    options.ConnectionString = services.GetPostgreSqlConnectionString();
                    options.UseSharedConnectionPool = true;
                }
            );
        }

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

            return setup.UsePostgreSql(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Configures the distributed-lock provider to use PostgreSQL advisory locks, binding
        /// <see cref="PostgreSqlDistributedLockOptions"/> from the supplied <see cref="IConfiguration"/>.
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

            setup.RegisterExtension(new PostgreSqlDistributedLocksOptionsExtension(configuration));

            return setup;
        }

        /// <summary>
        /// Configures the distributed-lock provider to use PostgreSQL advisory locks, applying the
        /// supplied delegate to <see cref="PostgreSqlDistributedLockOptions"/>.
        /// </summary>
        /// <param name="configure">
        /// A delegate that configures the options. Must not be <see langword="null"/>.
        /// </param>
        /// <returns>The same <see cref="HeadlessDistributedLocksSetupBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="configure"/> is <see langword="null"/>.
        /// </exception>
        public HeadlessDistributedLocksSetupBuilder UsePostgreSql(Action<PostgreSqlDistributedLockOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlDistributedLocksOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Configures the distributed-lock provider to use PostgreSQL advisory locks, applying the
        /// supplied delegate (which also receives the <see cref="IServiceProvider"/>) to
        /// <see cref="PostgreSqlDistributedLockOptions"/>.
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
            Action<PostgreSqlDistributedLockOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlDistributedLocksOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PostgreSqlDistributedLocksOptionsExtension : IDistributedLocksOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<PostgreSqlDistributedLockOptions>? _configure;
        private readonly Action<PostgreSqlDistributedLockOptions, IServiceProvider>? _configureWithServices;

        public PostgreSqlDistributedLocksOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public PostgreSqlDistributedLocksOptionsExtension(Action<PostgreSqlDistributedLockOptions> configure)
        {
            _configure = configure;
        }

        public PostgreSqlDistributedLocksOptionsExtension(
            Action<PostgreSqlDistributedLockOptions, IServiceProvider> configure
        )
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<PostgreSqlDistributedLockOptions, PostgreSqlDistributedLockOptionsValidator>(
                    _configuration
                );
            }
            else if (_configure is not null)
            {
                services.Configure<PostgreSqlDistributedLockOptions, PostgreSqlDistributedLockOptionsValidator>(
                    _configure
                );
            }
            else
            {
                services.Configure<PostgreSqlDistributedLockOptions, PostgreSqlDistributedLockOptionsValidator>(
                    _configureWithServices
                );
            }

            _AddPostgreSqlDistributedLocksCore(services);
        }
    }

    private static IServiceCollection _AddPostgreSqlDistributedLocksCore(IServiceCollection services)
    {
        services.AddOptions<DistributedLocksStorageOptions, PostgreSqlDistributedLocksStorageOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHeadlessGuidGenerator();

        // One connection source shared by storage, release signal, and schema runner, so a configuration produces a
        // single pool rather than one each. It is deliberately not registered as NpgsqlDataSource: that service is the
        // application's (AddNpgsqlDataSource, Aspire, EF Core), and the parameterless UsePostgreSql() reads it. Claiming
        // it would send locks to the application's database or application traffic to the lock database, depending on
        // which registration came first.
        services.TryAddSingleton<PostgresLockDataSource>();

        services.TryAddSingleton<PostgresConnectionScopedLockStorage>();
        services.TryAddSingleton<IConnectionScopedLockStorage>(sp =>
            sp.GetRequiredService<PostgresConnectionScopedLockStorage>()
        );
        services.TryAddSingleton<IReleaseSignal, PostgresReleaseSignal>();
        // The runner creates the fence sequence at startup on the same data source the exclusive acquires draw from, so
        // verify mode and the exported deploy script cover it like every other feature's objects.
        services.AddHeadlessSchemaContribution(sp =>
            PostgreSqlDistributedLocksSchemaContribution.Create(
                sp.GetRequiredService<IOptions<DistributedLocksStorageOptions>>().Value,
                sp.GetRequiredService<PostgresLockDataSource>()
            )
        );
        // The unit-of-work feature behind unit.TransactionLocks; resolved by GetFeature on a host that also registered
        // a unit-of-work provider, inert otherwise.
        services.TryAddSingleton<IUnitOfWorkTransactionLocks, PostgreSqlUnitOfWorkTransactionLocks>();
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
            pollingFallback: sp.GetRequiredService<IOptions<PostgreSqlDistributedLockOptions>>().Value.PollingFallback
        ));
        services.TryAddSingleton<IDistributedLock>(sp => sp.GetRequiredService<ConnectionScopedDistributedLock>());
        services.TryAddSingleton<IDistributedReadWriteLock, ConnectionScopedReadWriteLock>();

        return services;
    }

    private sealed class PostgreSqlDistributedLocksStorageOptionsValidator
        : AbstractValidator<DistributedLocksStorageOptions>
    {
        public PostgreSqlDistributedLocksStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
        }
    }
}
