// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.Permissions;
using Headless.Permissions.Repositories;
using Headless.Serializer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Permissions.SqlServer;

/// <summary>
/// Registers the SQL Server raw-DDL storage provider for Headless Permissions.
/// </summary>
[PublicAPI]
public static class SetupPermissionsSqlServer
{
    extension(HeadlessPermissionsSetupBuilder setup)
    {
        /// <summary>
        /// Configures the permissions system to use SQL Server for raw-DDL storage, setting the connection
        /// string directly.
        /// </summary>
        /// <param name="connectionString">The SQL Server connection string.</param>
        public HeadlessPermissionsSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Configures the permissions system to use SQL Server for raw-DDL storage, binding
        /// <see cref="SqlServerPermissionsOptions"/> from <paramref name="configuration"/>.
        /// </summary>
        /// <param name="configuration">
        /// The configuration section to bind into <see cref="SqlServerPermissionsOptions"/>.
        /// </param>
        public HeadlessPermissionsSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new SqlServerPermissionsOptionsExtension(configuration));

            return setup;
        }

        /// <summary>
        /// Configures the permissions system to use SQL Server for raw-DDL storage, applying
        /// <paramref name="configure"/> to <see cref="SqlServerPermissionsOptions"/>.
        /// </summary>
        /// <param name="configure">Delegate that configures the SQL Server provider options.</param>
        public HeadlessPermissionsSetupBuilder UseSqlServer(Action<SqlServerPermissionsOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerPermissionsOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Configures the permissions system to use SQL Server for raw-DDL storage, applying
        /// <paramref name="configure"/> to <see cref="SqlServerPermissionsOptions"/> with access to
        /// resolved services.
        /// </summary>
        /// <param name="configure">
        /// Delegate that configures the SQL Server provider options with access to <see cref="IServiceProvider"/>.
        /// </param>
        public HeadlessPermissionsSetupBuilder UseSqlServer(
            Action<SqlServerPermissionsOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerPermissionsOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class SqlServerPermissionsOptionsExtension : IPermissionsStorageOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<SqlServerPermissionsOptions>? _configure;
        private readonly Action<SqlServerPermissionsOptions, IServiceProvider>? _configureWithServices;

        public SqlServerPermissionsOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public SqlServerPermissionsOptionsExtension(Action<SqlServerPermissionsOptions> configure)
        {
            _configure = configure;
        }

        public SqlServerPermissionsOptionsExtension(Action<SqlServerPermissionsOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<SqlServerPermissionsOptions, SqlServerPermissionsOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<SqlServerPermissionsOptions, SqlServerPermissionsOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<SqlServerPermissionsOptions, SqlServerPermissionsOptionsValidator>(
                    _configureWithServices
                );
            }

            services.AddOptions<PermissionsStorageOptions, SqlServerPermissionsStorageOptionsValidator>();
            services.AddInitializerHostedService<SqlServerPermissionsStorageInitializer>();
            services.TryAddSingleton<IJsonSerializer>(_ => new SystemJsonSerializer());
            services.TryAddSingleton<IPermissionGrantRepository, SqlServerPermissionGrantRepository>();
            services.TryAddSingleton<
                IPermissionDefinitionRecordRepository,
                SqlServerPermissionDefinitionRecordRepository
            >();
        }
    }

    private sealed class SqlServerPermissionsStorageOptionsValidator : AbstractValidator<PermissionsStorageOptions>
    {
        public SqlServerPermissionsStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidSqlServerIdentifier();
            RuleFor(x => x.PermissionGrantsTableName).IsValidSqlServerIdentifier();
            RuleFor(x => x.PermissionDefinitionsTableName).IsValidSqlServerIdentifier();
            RuleFor(x => x.PermissionGroupDefinitionsTableName).IsValidSqlServerIdentifier();
        }
    }
}
