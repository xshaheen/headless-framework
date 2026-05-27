// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.Settings;
using Headless.Settings.PostgreSql;
using Headless.Settings.Repositories;
using Headless.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupSettingsPostgreSql
{
    extension(HeadlessSettingsSetupBuilder setup)
    {
        public HeadlessSettingsSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessSettingsSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new PostgreSqlSettingsOptionsExtension(configuration));

            return setup;
        }

        public HeadlessSettingsSetupBuilder UsePostgreSql(Action<PostgreSqlSettingsOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlSettingsOptionsExtension(configure));

            return setup;
        }

        public HeadlessSettingsSetupBuilder UsePostgreSql(
            Action<PostgreSqlSettingsOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlSettingsOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PostgreSqlSettingsOptionsExtension : ISettingsStorageOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<PostgreSqlSettingsOptions>? _configure;
        private readonly Action<PostgreSqlSettingsOptions, IServiceProvider>? _configureWithServices;

        public PostgreSqlSettingsOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public PostgreSqlSettingsOptionsExtension(Action<PostgreSqlSettingsOptions> configure)
        {
            _configure = configure;
        }

        public PostgreSqlSettingsOptionsExtension(Action<PostgreSqlSettingsOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<PostgreSqlSettingsOptions, PostgreSqlSettingsOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<PostgreSqlSettingsOptions, PostgreSqlSettingsOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<PostgreSqlSettingsOptions, PostgreSqlSettingsOptionsValidator>(
                    _configureWithServices
                );
            }

            services.AddOptions<SettingsStorageOptions, PostgreSqlSettingsStorageOptionsValidator>();
            services.AddInitializerHostedService<PostgreSqlSettingsStorageInitializer>();
            services.TryAddSingleton<ISettingValueRecordRepository, PostgreSqlSettingValueRecordRepository>();
            services.TryAddSingleton<ISettingDefinitionRecordRepository, PostgreSqlSettingDefinitionRecordRepository>();
        }
    }

    private sealed class PostgreSqlSettingsStorageOptionsValidator : AbstractValidator<SettingsStorageOptions>
    {
        public PostgreSqlSettingsStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
            RuleFor(x => x.SettingValuesTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
            RuleFor(x => x.SettingDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
        }
    }
}
