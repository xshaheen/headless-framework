// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Fencing.PostgreSql;
using Headless.UnitOfWork;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Fencing;

/// <summary>Chooses PostgreSQL as the fencing provider.</summary>
[PublicAPI]
public static class SetupFencingPostgreSql
{
    extension(HeadlessFencingSetupBuilder setup)
    {
        /// <summary>Stores leases in PostgreSQL, in the database named by <paramref name="connectionString" />.</summary>
        /// <param name="connectionString">The Npgsql connection string.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="PostgreSqlFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose Npgsql connection reaches this same database.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="connectionString" /> is <see langword="null" /> or whitespace.</exception>
        public HeadlessFencingSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Stores leases in PostgreSQL, binding <see cref="PostgreSqlFencingOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="PostgreSqlFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose Npgsql connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessFencingSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new PostgreSqlFencingOptionsExtension(configuration));

            return setup;
        }

        /// <summary>Stores leases in PostgreSQL, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="PostgreSqlFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose Npgsql connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessFencingSetupBuilder UsePostgreSql(Action<PostgreSqlFencingOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlFencingOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Stores leases in PostgreSQL, configured by <paramref name="configure" /> with access to the application
        /// services.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="PostgreSqlFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose Npgsql connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessFencingSetupBuilder UsePostgreSql(Action<PostgreSqlFencingOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlFencingOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PostgreSqlFencingOptionsExtension : IFencingProviderOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<PostgreSqlFencingOptions>? _configure;
        private readonly Action<PostgreSqlFencingOptions, IServiceProvider>? _configureWithServices;

        public PostgreSqlFencingOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public PostgreSqlFencingOptionsExtension(Action<PostgreSqlFencingOptions> configure)
        {
            _configure = configure;
        }

        public PostgreSqlFencingOptionsExtension(Action<PostgreSqlFencingOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<PostgreSqlFencingOptions, PostgreSqlFencingOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<PostgreSqlFencingOptions, PostgreSqlFencingOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<PostgreSqlFencingOptions, PostgreSqlFencingOptionsValidator>(_configureWithServices);
            }

            services.AddOptions<FencingStorageOptions, PostgreSqlFencingStorageOptionsValidator>();

            // Sweeps begin owned units through the unit-of-work factory, and enlisted calls reach the store through
            // unit.Leases, so the factory must exist whether or not the host registered it.
            services.AddPostgreSqlUnitOfWork();

            services.AddInitializerHostedService<PostgreSqlFencingStorageInitializer>();
            services.TryAddSingleton<ILeaseStore, PostgreSqlLeaseStore>();
        }
    }
}
