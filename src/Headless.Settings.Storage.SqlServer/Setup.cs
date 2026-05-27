// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.Settings;
using Headless.Settings.Repositories;
using Headless.Settings.SqlServer;
using Headless.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupSettingsSqlServer
{
    extension(HeadlessSettingsSetupBuilder setup)
    {
        public HeadlessSettingsSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessSettingsSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new SqlServerSettingsOptionsExtension(configuration));

            return setup;
        }

        public HeadlessSettingsSetupBuilder UseSqlServer(Action<SqlServerSettingsOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerSettingsOptionsExtension(configure));

            return setup;
        }

        public HeadlessSettingsSetupBuilder UseSqlServer(
            Action<SqlServerSettingsOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerSettingsOptionsExtension(configure));

            return setup;
        }
    }

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

            services.AddOptions<SettingsStorageOptions, SqlServerSettingsStorageOptionsValidator>();
            services.AddInitializerHostedService<SqlServerSettingsStorageInitializer>();
            services.TryAddSingleton<ISettingValueRecordRepository, SqlServerSettingValueRecordRepository>();
            services.TryAddSingleton<ISettingDefinitionRecordRepository, SqlServerSettingDefinitionRecordRepository>();
        }
    }

    private sealed class SqlServerSettingsStorageOptionsValidator : AbstractValidator<SettingsStorageOptions>
    {
        public SqlServerSettingsStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
            RuleFor(x => x.SettingValuesTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
            RuleFor(x => x.SettingDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
        }
    }
}
