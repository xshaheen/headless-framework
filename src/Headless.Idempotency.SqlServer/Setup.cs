// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Idempotency.SqlServer;
using Headless.UnitOfWork;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Idempotency;

/// <summary>Chooses SQL Server as the idempotency provider.</summary>
[PublicAPI]
public static class SetupIdempotencySqlServer
{
    extension(HeadlessIdempotencySetupBuilder setup)
    {
        /// <summary>
        /// Stores idempotency records in SQL Server, in the database named by <paramref name="connectionString" />,
        /// which must be the database that holds the fenced leases.
        /// </summary>
        /// <param name="connectionString">The SqlClient connection string.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The record table is created at host startup unless
        /// <see cref="SqlServerIdempotencyOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted
        /// call is accepted only on a unit whose SqlClient connection reaches this same database.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="connectionString" /> is <see langword="null" /> or whitespace.</exception>
        public HeadlessIdempotencySetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Stores idempotency records in SQL Server, binding <see cref="SqlServerIdempotencyOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The record table is created at host startup unless
        /// <see cref="SqlServerIdempotencyOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted
        /// call is accepted only on a unit whose SqlClient connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessIdempotencySetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new SqlServerIdempotencyOptionsExtension(configuration));

            return setup;
        }

        /// <summary>Stores idempotency records in SQL Server, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The record table is created at host startup unless
        /// <see cref="SqlServerIdempotencyOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted
        /// call is accepted only on a unit whose SqlClient connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessIdempotencySetupBuilder UseSqlServer(Action<SqlServerIdempotencyOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerIdempotencyOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Stores idempotency records in SQL Server, configured by <paramref name="configure" /> with access to the
        /// application services.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The record table is created at host startup unless
        /// <see cref="SqlServerIdempotencyOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted
        /// call is accepted only on a unit whose SqlClient connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessIdempotencySetupBuilder UseSqlServer(
            Action<SqlServerIdempotencyOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerIdempotencyOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class SqlServerIdempotencyOptionsExtension : IIdempotencyProviderOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<SqlServerIdempotencyOptions>? _configure;
        private readonly Action<SqlServerIdempotencyOptions, IServiceProvider>? _configureWithServices;

        public SqlServerIdempotencyOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public SqlServerIdempotencyOptionsExtension(Action<SqlServerIdempotencyOptions> configure)
        {
            _configure = configure;
        }

        public SqlServerIdempotencyOptionsExtension(Action<SqlServerIdempotencyOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<SqlServerIdempotencyOptions, SqlServerIdempotencyOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<SqlServerIdempotencyOptions, SqlServerIdempotencyOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<SqlServerIdempotencyOptions, SqlServerIdempotencyOptionsValidator>(
                    _configureWithServices
                );
            }

            services.AddOptions<IdempotencyStorageOptions, SqlServerIdempotencyStorageOptionsValidator>();

            // Autonomous calls begin owned units through the unit-of-work factory, and enlisted calls reach the store
            // through unit.Idempotency, so the factory must exist whether or not the host registered it.
            services.AddSqlServerUnitOfWork();

            services.AddInitializerHostedService<SqlServerIdempotencyStorageInitializer>();
            services.TryAddSingleton<IIdempotencyRecordStore, SqlServerIdempotencyRecordStore>();
        }
    }
}
