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

[PublicAPI]
public static class SetupPermissionsPostgreSql
{
    extension(HeadlessPermissionsSetupBuilder setup)
    {
        public HeadlessPermissionsSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessPermissionsSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new PostgreSqlPermissionsOptionsExtension(configuration));

            return setup;
        }

        public HeadlessPermissionsSetupBuilder UsePostgreSql(Action<PostgreSqlPermissionsOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlPermissionsOptionsExtension(configure));

            return setup;
        }

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
            services.TryAddSingleton<IPermissionDefinitionRecordRepository, PostgreSqlPermissionDefinitionRecordRepository>();
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
