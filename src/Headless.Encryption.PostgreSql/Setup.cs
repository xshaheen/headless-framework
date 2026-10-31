// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Encryption.PostgreSql;
using Headless.Hosting.Schema;
using Headless.Sql;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Encryption;

/// <summary>Chooses PostgreSQL as the encryption key store.</summary>
[PublicAPI]
public static class SetupEncryptionPostgreSql
{
    extension(HeadlessEncryptionSetupBuilder setup)
    {
        /// <summary>
        /// Stores the wrapped data keys in the database of the connection registered by <c>AddPostgreSqlSql</c>, so the keys
        /// live beside the data they protect.
        /// </summary>
        /// <returns>The setup builder for chaining.</returns>
        /// <remarks>
        /// Options resolution throws <see cref="InvalidOperationException"/> when <c>AddPostgreSqlSql</c> was not called or
        /// registered another provider's connection.
        /// </remarks>
        public HeadlessEncryptionSetupBuilder UsePostgreSql()
        {
            return setup.UsePostgreSql(
                (options, services) => options.ConnectionString = services.GetPostgreSqlConnectionString()
            );
        }

        /// <summary>Stores the wrapped data keys in PostgreSQL, in the database named by <paramref name="connectionString" />.</summary>
        /// <param name="connectionString">The Npgsql connection string.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>The key table is created by <c>resources provision</c> and verified at host start.</remarks>
        /// <exception cref="ArgumentException"><paramref name="connectionString" /> is <see langword="null" /> or whitespace.</exception>
        public HeadlessEncryptionSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Stores the wrapped data keys in PostgreSQL, binding <see cref="PostgreSqlEncryptionKeyStoreOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the store options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>The key table is created by <c>resources provision</c> and verified at host start.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessEncryptionSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterKeyStore(new PostgreSqlKeyStoreOptionsExtension(configuration));

            return setup;
        }

        /// <summary>Stores the wrapped data keys in PostgreSQL, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the store options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>The key table is created by <c>resources provision</c> and verified at host start.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessEncryptionSetupBuilder UsePostgreSql(Action<PostgreSqlEncryptionKeyStoreOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterKeyStore(new PostgreSqlKeyStoreOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Stores the wrapped data keys in PostgreSQL, configured by <paramref name="configure" /> with access to the
        /// application services.
        /// </summary>
        /// <param name="configure">Delegate that configures the store options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>The key table is created by <c>resources provision</c> and verified at host start.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessEncryptionSetupBuilder UsePostgreSql(
            Action<PostgreSqlEncryptionKeyStoreOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterKeyStore(new PostgreSqlKeyStoreOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PostgreSqlKeyStoreOptionsExtension : IEncryptionProviderOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<PostgreSqlEncryptionKeyStoreOptions>? _configure;
        private readonly Action<PostgreSqlEncryptionKeyStoreOptions, IServiceProvider>? _configureWithServices;

        public PostgreSqlKeyStoreOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public PostgreSqlKeyStoreOptionsExtension(Action<PostgreSqlEncryptionKeyStoreOptions> configure)
        {
            _configure = configure;
        }

        public PostgreSqlKeyStoreOptionsExtension(
            Action<PostgreSqlEncryptionKeyStoreOptions, IServiceProvider> configure
        )
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<PostgreSqlEncryptionKeyStoreOptions, PostgreSqlEncryptionKeyStoreOptionsValidator>(
                    _configuration
                );
            }
            else if (_configure is not null)
            {
                services.Configure<PostgreSqlEncryptionKeyStoreOptions, PostgreSqlEncryptionKeyStoreOptionsValidator>(
                    _configure
                );
            }
            else
            {
                services.Configure<PostgreSqlEncryptionKeyStoreOptions, PostgreSqlEncryptionKeyStoreOptionsValidator>(
                    _configureWithServices
                );
            }

            // The contribution factory reads options at first resolution, so configured names reach the steps.
            services.AddHeadlessSchemaContribution(sp =>
                PostgreSqlEncryptionSchemaContribution.Create(
                    sp.GetRequiredService<IOptions<PostgreSqlEncryptionKeyStoreOptions>>().Value
                )
            );
            services.TryAddSingleton<IDataKeyStore>(sp => new RelationalDataKeyStore(
                PostgreSqlDialect.Instance,
                sp.GetRequiredService<IOptions<PostgreSqlEncryptionKeyStoreOptions>>().Value,
                sp.GetRequiredService<TimeProvider>()
            ));
        }
    }
}
