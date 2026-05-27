// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.Permissions;
using Headless.Permissions.Repositories;
using Headless.Permissions.SqlServer;
using Headless.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupPermissionsSqlServer
{
    extension(HeadlessPermissionsSetupBuilder setup)
    {
        public HeadlessPermissionsSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessPermissionsSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new SqlServerPermissionsOptionsExtension(configuration));

            return setup;
        }

        public HeadlessPermissionsSetupBuilder UseSqlServer(Action<SqlServerPermissionsOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerPermissionsOptionsExtension(configure));

            return setup;
        }

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
                services.Configure<SqlServerPermissionsOptions, SqlServerPermissionsOptionsValidator>(_configureWithServices);
            }

            services.AddOptions<PermissionsStorageOptions, SqlServerPermissionsStorageOptionsValidator>();
            services.AddInitializerHostedService<SqlServerPermissionsStorageInitializer>();
            services.TryAddSingleton<IPermissionGrantRepository, SqlServerPermissionGrantRepository>();
            services.TryAddSingleton<IPermissionDefinitionRecordRepository, SqlServerPermissionDefinitionRecordRepository>();
        }
    }

    private sealed class SqlServerPermissionsStorageOptionsValidator : AbstractValidator<PermissionsStorageOptions>
    {
        public SqlServerPermissionsStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
            RuleFor(x => x.PermissionGrantsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
            RuleFor(x => x.PermissionDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
            RuleFor(x => x.PermissionGroupDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
        }
    }
}
