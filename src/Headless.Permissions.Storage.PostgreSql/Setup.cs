// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Constants;
using Headless.Permissions.PostgreSql;
using Headless.Sql;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Permissions;

/// <summary>
/// Registers the PostgreSQL raw-DDL storage provider for Headless Permissions.
/// </summary>
[PublicAPI]
public static class SetupPermissionsPostgreSql
{
    extension(HeadlessPermissionsSetupBuilder setup)
    {
        /// <summary>
        /// Configures PostgreSQL storage with the connection registered by <c>AddPostgreSqlSql</c>, so one
        /// connection string serves every feature that shares the database.
        /// </summary>
        /// <returns>The setup builder for chaining.</returns>
        /// <remarks>
        /// Options resolution throws <see cref="InvalidOperationException"/> when <c>AddPostgreSqlSql</c> was not
        /// called or registered another provider's connection.
        /// </remarks>
        public HeadlessPermissionsSetupBuilder UsePostgreSql()
        {
            return setup.UsePostgreSql(
                (options, services) => options.ConnectionString = services.GetPostgreSqlConnectionString()
            );
        }

        /// <summary>
        /// Configures the permissions system to use PostgreSQL for raw-DDL storage, setting the connection
        /// string directly.
        /// </summary>
        /// <param name="connectionString">The Npgsql connection string for the PostgreSQL database.</param>
        public HeadlessPermissionsSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Configures the permissions system to use PostgreSQL for raw-DDL storage, binding
        /// <see cref="PostgreSqlPermissionsOptions"/> from <paramref name="configuration"/>.
        /// </summary>
        /// <param name="configuration">
        /// The configuration section to bind into <see cref="PostgreSqlPermissionsOptions"/>.
        /// </param>
        public HeadlessPermissionsSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new PostgreSqlPermissionsOptionsExtension(configuration));

            return setup;
        }

        /// <summary>
        /// Configures the permissions system to use PostgreSQL for raw-DDL storage, applying
        /// <paramref name="configure"/> to <see cref="PostgreSqlPermissionsOptions"/>.
        /// </summary>
        /// <param name="configure">Delegate that configures the PostgreSQL provider options.</param>
        public HeadlessPermissionsSetupBuilder UsePostgreSql(Action<PostgreSqlPermissionsOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlPermissionsOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Configures the permissions system to use PostgreSQL for raw-DDL storage, applying
        /// <paramref name="configure"/> to <see cref="PostgreSqlPermissionsOptions"/> with access to
        /// resolved services.
        /// </summary>
        /// <param name="configure">
        /// Delegate that configures the PostgreSQL provider options with access to <see cref="IServiceProvider"/>.
        /// </param>
        public HeadlessPermissionsSetupBuilder UsePostgreSql(
            Action<PostgreSqlPermissionsOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlPermissionsOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PostgreSqlPermissionsOptionsExtension : IPermissionsStorageOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<PostgreSqlPermissionsOptions>? _configure;
        private readonly Action<PostgreSqlPermissionsOptions, IServiceProvider>? _configureWithServices;

        public PostgreSqlPermissionsOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public PostgreSqlPermissionsOptionsExtension(Action<PostgreSqlPermissionsOptions> configure)
        {
            _configure = configure;
        }

        public PostgreSqlPermissionsOptionsExtension(Action<PostgreSqlPermissionsOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<PostgreSqlPermissionsOptions, PostgreSqlPermissionsOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<PostgreSqlPermissionsOptions, PostgreSqlPermissionsOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<PostgreSqlPermissionsOptions, PostgreSqlPermissionsOptionsValidator>(
                    _configureWithServices
                );
            }

            RelationalPermissionsStorage.AddServices<PostgreSqlPermissionsOptions>(
                services,
                PostgreSqlDialect.Instance,
                StorageProvider.PostgreSql,
                PostgreSqlPermissionsSchemaContribution.Create
            );
        }
    }
}
