// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Constants;
using Headless.Settings.Repositories;
using Headless.Settings.SqlServer;
using Headless.Sql;
using Headless.Sql.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Settings;

/// <summary>Extension members that configure the SQL Server storage backend for the Headless settings feature.</summary>
[PublicAPI]
public static class SetupSettingsSqlServer
{
    extension(HeadlessSettingsSetupBuilder setup)
    {
        /// <summary>
        /// Configures SQL Server storage with the connection registered by <c>AddSqlServerSql</c>, so one
        /// connection string serves every feature that shares the database.
        /// </summary>
        /// <returns>The setup builder for chaining.</returns>
        /// <remarks>
        /// Options resolution throws <see cref="InvalidOperationException"/> when <c>AddSqlServerSql</c> was not
        /// called or registered another provider's connection.
        /// </remarks>
        public HeadlessSettingsSetupBuilder UseSqlServer()
        {
            return setup.UseSqlServer(
                (options, services) => options.ConnectionString = services.GetSqlServerConnectionString()
            );
        }

        /// <summary>Configures the settings feature to use SQL Server, setting the connection string directly.</summary>
        /// <param name="connectionString">SQL Server connection string. Must not be <see langword="null"/>, empty, or white space.</param>
        /// <returns>The same <see cref="HeadlessSettingsSetupBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="connectionString"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty or white space.</exception>
        public HeadlessSettingsSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options => options.ConnectionString = connectionString);
        }

        /// <summary>Configures the settings feature to use SQL Server, binding options from <paramref name="configuration"/>.</summary>
        /// <param name="configuration">Configuration section to bind to <see cref="SqlServerSettingsOptions"/>. Must not be <see langword="null"/>.</param>
        /// <returns>The same <see cref="HeadlessSettingsSetupBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessSettingsSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new SqlServerSettingsOptionsExtension(configuration));

            return setup;
        }

        /// <summary>Configures the settings feature to use SQL Server, applying <paramref name="configure"/> to the provider options.</summary>
        /// <param name="configure">Delegate used to configure <see cref="SqlServerSettingsOptions"/>. Must not be <see langword="null"/>.</param>
        /// <returns>The same <see cref="HeadlessSettingsSetupBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessSettingsSetupBuilder UseSqlServer(Action<SqlServerSettingsOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerSettingsOptionsExtension(configure));

            return setup;
        }

        /// <summary>Configures the settings feature to use SQL Server, applying <paramref name="configure"/> to the provider options with access to the <see cref="IServiceProvider"/>.</summary>
        /// <param name="configure">Delegate used to configure <see cref="SqlServerSettingsOptions"/> with service resolution. Must not be <see langword="null"/>.</param>
        /// <returns>The same <see cref="HeadlessSettingsSetupBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessSettingsSetupBuilder UseSqlServer(Action<SqlServerSettingsOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerSettingsOptionsExtension(configure));

            return setup;
        }
    }

    /// <summary>Wires the SQL Server provider services into the DI container when applied to the settings setup builder.</summary>
    private sealed class SqlServerSettingsOptionsExtension : ISettingsStorageOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<SqlServerSettingsOptions>? _configure;
        private readonly Action<SqlServerSettingsOptions, IServiceProvider>? _configureWithServices;

        public SqlServerSettingsOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public SqlServerSettingsOptionsExtension(Action<SqlServerSettingsOptions> configure)
        {
            _configure = configure;
        }

        public SqlServerSettingsOptionsExtension(Action<SqlServerSettingsOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<SqlServerSettingsOptions, SqlServerSettingsOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<SqlServerSettingsOptions, SqlServerSettingsOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<SqlServerSettingsOptions, SqlServerSettingsOptionsValidator>(_configureWithServices);
            }

            RelationalSettingsStorage.AddServices<SqlServerSettingsOptions>(
                services,
                SqlServerDialect.Instance,
                StorageProvider.SqlServer,
                SqlServerSettingsSchemaContribution.Create
            );
        }
    }
}
