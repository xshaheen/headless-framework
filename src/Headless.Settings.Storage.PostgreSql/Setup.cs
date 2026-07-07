// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.Serializer;
using Headless.Settings;
using Headless.Settings.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Settings.PostgreSql;

/// <summary>Extension members that configure the PostgreSQL storage backend for the Headless settings feature.</summary>
[PublicAPI]
public static class SetupSettingsPostgreSql
{
    extension(HeadlessSettingsSetupBuilder setup)
    {
        /// <summary>Configures the settings feature to use PostgreSQL, setting the connection string directly.</summary>
        /// <param name="connectionString">PostgreSQL connection string. Must not be <see langword="null"/>, empty, or white space.</param>
        /// <returns>The same <see cref="HeadlessSettingsSetupBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="connectionString"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty or white space.</exception>
        public HeadlessSettingsSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options => options.ConnectionString = connectionString);
        }

        /// <summary>Configures the settings feature to use PostgreSQL, binding options from <paramref name="configuration"/>.</summary>
        /// <param name="configuration">Configuration section to bind to <see cref="PostgreSqlSettingsOptions"/>. Must not be <see langword="null"/>.</param>
        /// <returns>The same <see cref="HeadlessSettingsSetupBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessSettingsSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new PostgreSqlSettingsOptionsExtension(configuration));

            return setup;
        }

        /// <summary>Configures the settings feature to use PostgreSQL, applying <paramref name="configure"/> to the provider options.</summary>
        /// <param name="configure">Delegate used to configure <see cref="PostgreSqlSettingsOptions"/>. Must not be <see langword="null"/>.</param>
        /// <returns>The same <see cref="HeadlessSettingsSetupBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessSettingsSetupBuilder UsePostgreSql(Action<PostgreSqlSettingsOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlSettingsOptionsExtension(configure));

            return setup;
        }

        /// <summary>Configures the settings feature to use PostgreSQL, applying <paramref name="configure"/> to the provider options with access to the <see cref="IServiceProvider"/>.</summary>
        /// <param name="configure">Delegate used to configure <see cref="PostgreSqlSettingsOptions"/> with service resolution. Must not be <see langword="null"/>.</param>
        /// <returns>The same <see cref="HeadlessSettingsSetupBuilder"/> instance to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessSettingsSetupBuilder UsePostgreSql(Action<PostgreSqlSettingsOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlSettingsOptionsExtension(configure));

            return setup;
        }
    }

    /// <summary>Wires the PostgreSQL provider services into the DI container when applied to the settings setup builder.</summary>
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
            services.TryAddSingleton<IJsonSerializer>(_ => new SystemJsonSerializer());
            services.TryAddSingleton<ISettingValueRecordRepository, PostgreSqlSettingValueRecordRepository>();
            services.TryAddSingleton<ISettingDefinitionRecordRepository, PostgreSqlSettingDefinitionRecordRepository>();
        }
    }

    /// <summary>Validates <see cref="SettingsStorageOptions"/> for use with the PostgreSQL backend, ensuring schema and table name identifiers are valid.</summary>
    private sealed class PostgreSqlSettingsStorageOptionsValidator : AbstractValidator<SettingsStorageOptions>
    {
        public PostgreSqlSettingsStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidPostgreSqlIdentifier();
            RuleFor(x => x.SettingValuesTableName).IsValidPostgreSqlIdentifier();
            RuleFor(x => x.SettingDefinitionsTableName).IsValidPostgreSqlIdentifier();
        }
    }
}
