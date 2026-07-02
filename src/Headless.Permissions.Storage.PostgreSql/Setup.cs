// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.Permissions;
using Headless.Permissions.PostgreSql;
using Headless.Permissions.Repositories;
using Headless.Serializer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the PostgreSQL raw-DDL storage provider for Headless Permissions.
/// </summary>
[PublicAPI]
public static class SetupPermissionsPostgreSql
{
    extension(HeadlessPermissionsSetupBuilder setup)
    {
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

            services.AddOptions<PermissionsStorageOptions, PostgreSqlPermissionsStorageOptionsValidator>();
            services.AddInitializerHostedService<PostgreSqlPermissionsStorageInitializer>();
            services.TryAddSingleton<IJsonSerializer>(_ => new SystemJsonSerializer());
            services.TryAddSingleton<IPermissionGrantRepository, PostgreSqlPermissionGrantRepository>();
            services.TryAddSingleton<
                IPermissionDefinitionRecordRepository,
                PostgreSqlPermissionDefinitionRecordRepository
            >();
        }
    }

    private sealed class PostgreSqlPermissionsStorageOptionsValidator : AbstractValidator<PermissionsStorageOptions>
    {
        public PostgreSqlPermissionsStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidPostgreSqlIdentifier();
            RuleFor(x => x.PermissionGrantsTableName).IsValidPostgreSqlIdentifier();
            RuleFor(x => x.PermissionDefinitionsTableName).IsValidPostgreSqlIdentifier();
            RuleFor(x => x.PermissionGroupDefinitionsTableName).IsValidPostgreSqlIdentifier();
        }
    }
}
