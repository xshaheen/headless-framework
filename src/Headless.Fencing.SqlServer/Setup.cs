// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Fencing.SqlServer;
using Headless.UnitOfWork;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Fencing;

/// <summary>Chooses SQL Server as the fencing provider.</summary>
[PublicAPI]
public static class SetupFencingSqlServer
{
    extension(HeadlessFencingSetupBuilder setup)
    {
        /// <summary>Stores leases in SQL Server, in the database named by <paramref name="connectionString" />.</summary>
        /// <param name="connectionString">The SqlClient connection string.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="SqlServerFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose SqlClient connection reaches this same database.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="connectionString" /> is <see langword="null" /> or whitespace.</exception>
        public HeadlessFencingSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Stores leases in SQL Server, binding <see cref="SqlServerFencingOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="SqlServerFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose SqlClient connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessFencingSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new SqlServerFencingOptionsExtension(configuration));

            return setup;
        }

        /// <summary>Stores leases in SQL Server, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the provider options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="SqlServerFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose SqlClient connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessFencingSetupBuilder UseSqlServer(Action<SqlServerFencingOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerFencingOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Stores leases in SQL Server, configured by <paramref name="configure" /> with access to the application
        /// services.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// The lease table is created at host startup unless
        /// <see cref="SqlServerFencingOptions.InitializeOnStartup" /> is <see langword="false" />. An enlisted call
        /// is accepted only on a unit whose SqlClient connection reaches the configured database.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessFencingSetupBuilder UseSqlServer(Action<SqlServerFencingOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerFencingOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class SqlServerFencingOptionsExtension : IFencingProviderOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<SqlServerFencingOptions>? _configure;
        private readonly Action<SqlServerFencingOptions, IServiceProvider>? _configureWithServices;

        public SqlServerFencingOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public SqlServerFencingOptionsExtension(Action<SqlServerFencingOptions> configure)
        {
            _configure = configure;
        }

        public SqlServerFencingOptionsExtension(Action<SqlServerFencingOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<SqlServerFencingOptions, SqlServerFencingOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<SqlServerFencingOptions, SqlServerFencingOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<SqlServerFencingOptions, SqlServerFencingOptionsValidator>(_configureWithServices);
            }

            services.AddOptions<FencingStorageOptions, SqlServerFencingStorageOptionsValidator>();

            // Sweeps begin owned units through the unit-of-work factory, and enlisted calls reach the store through
            // unit.Leases, so the factory must exist whether or not the host registered it.
            services.AddSqlServerUnitOfWork();

            services.AddInitializerHostedService<SqlServerFencingStorageInitializer>();
            services.TryAddSingleton<ILeaseStore, SqlServerLeaseStore>();
        }
    }
}
