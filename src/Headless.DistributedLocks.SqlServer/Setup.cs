// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.DistributedLocks.SqlServer;

/// <summary>
/// Extension members on <see cref="HeadlessDistributedLocksSetupBuilder"/> that wire the SQL Server
/// <c>sp_getapplock</c>-backed provider. Use one of the <c>UseSqlServer</c> overloads from within
/// <c>AddHeadlessDistributedLocks</c> to register <see cref="IDistributedLock"/> and
/// <see cref="IDistributedReadWriteLock"/> backed by SQL Server session-scoped application locks.
/// </summary>
[PublicAPI]
public static class SetupSqlServerDistributedLocks
{
    extension(HeadlessDistributedLocksSetupBuilder setup)
    {
        /// <summary>
        /// Registers the SQL Server distributed-lock provider using the supplied connection string.
        /// </summary>
        /// <param name="connectionString">
        /// A SQL Server connection string. Must not be <see langword="null"/>, empty, or whitespace.
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="connectionString"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is empty or whitespace.</exception>
        public HeadlessDistributedLocksSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Registers the SQL Server distributed-lock provider and binds <see cref="SqlServerDistributedLockOptions"/>
        /// from the supplied <see cref="IConfiguration"/> section.
        /// </summary>
        /// <param name="configuration">
        /// Configuration section to bind into <see cref="SqlServerDistributedLockOptions"/>. Must not be <see langword="null"/>.
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessDistributedLocksSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new SqlServerDistributedLocksOptionsExtension(configuration));

            return setup;
        }

        /// <summary>
        /// Registers the SQL Server distributed-lock provider and configures <see cref="SqlServerDistributedLockOptions"/>
        /// using the supplied delegate.
        /// </summary>
        /// <param name="configure">
        /// Delegate that configures <see cref="SqlServerDistributedLockOptions"/>. Must not be <see langword="null"/>.
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessDistributedLocksSetupBuilder UseSqlServer(Action<SqlServerDistributedLockOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerDistributedLocksOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Registers the SQL Server distributed-lock provider and configures <see cref="SqlServerDistributedLockOptions"/>
        /// using the supplied delegate that also receives the <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="configure">
        /// Delegate that configures <see cref="SqlServerDistributedLockOptions"/> with access to the DI container.
        /// Must not be <see langword="null"/>.
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
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
