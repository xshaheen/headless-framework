// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Idempotency.PostgreSql;
using Headless.UnitOfWork;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Idempotency;

/// <summary>Chooses PostgreSQL as the idempotency provider.</summary>
[PublicAPI]
public static class SetupIdempotencyPostgreSql
{
    extension(HeadlessIdempotencySetupBuilder setup)
    {
        /// <summary>
        /// Stores idempotency records in PostgreSQL, in the database named by <paramref name="connectionString" />,
        /// which must be the database that holds the fenced leases.
        /// </summary>
        /// <param name="connectionString">The Npgsql connection string.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The record table is created at host startup unless
        /// <see cref="PostgreSqlIdempotencyOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted
        /// call is accepted only on a unit whose Npgsql connection reaches this same database.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="connectionString" /> is <see langword="null" /> or whitespace.</exception>
        public HeadlessIdempotencySetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Stores idempotency records in PostgreSQL, binding <see cref="PostgreSqlIdempotencyOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The record table is created at host startup unless
        /// <see cref="PostgreSqlIdempotencyOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted
        /// call is accepted only on a unit whose Npgsql connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessIdempotencySetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new PostgreSqlIdempotencyOptionsExtension(configuration));

            return setup;
        }

        /// <summary>Stores idempotency records in PostgreSQL, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The record table is created at host startup unless
        /// <see cref="PostgreSqlIdempotencyOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted
        /// call is accepted only on a unit whose Npgsql connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessIdempotencySetupBuilder UsePostgreSql(Action<PostgreSqlIdempotencyOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlIdempotencyOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Stores idempotency records in PostgreSQL, configured by <paramref name="configure" /> with access to the
        /// application services.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The record table is created at host startup unless
        /// <see cref="PostgreSqlIdempotencyOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted
        /// call is accepted only on a unit whose Npgsql connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessIdempotencySetupBuilder UsePostgreSql(
            Action<PostgreSqlIdempotencyOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlIdempotencyOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PostgreSqlIdempotencyOptionsExtension : IIdempotencyProviderOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<PostgreSqlIdempotencyOptions>? _configure;
        private readonly Action<PostgreSqlIdempotencyOptions, IServiceProvider>? _configureWithServices;

        public PostgreSqlIdempotencyOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public PostgreSqlIdempotencyOptionsExtension(Action<PostgreSqlIdempotencyOptions> configure)
        {
            _configure = configure;
        }

        public PostgreSqlIdempotencyOptionsExtension(Action<PostgreSqlIdempotencyOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<PostgreSqlIdempotencyOptions, PostgreSqlIdempotencyOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<PostgreSqlIdempotencyOptions, PostgreSqlIdempotencyOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<PostgreSqlIdempotencyOptions, PostgreSqlIdempotencyOptionsValidator>(
                    _configureWithServices
                );
            }

            services.AddOptions<IdempotencyStorageOptions, PostgreSqlIdempotencyStorageOptionsValidator>();

            // Autonomous calls begin owned units through the unit-of-work factory, and enlisted calls reach the store
            // through unit.Idempotency, so the factory must exist whether or not the host registered it.
            services.AddPostgreSqlUnitOfWork();

            services.AddInitializerHostedService<PostgreSqlIdempotencyStorageInitializer>();
            services.TryAddSingleton<IIdempotencyRecordStore, PostgreSqlIdempotencyRecordStore>();
        }
    }
}
