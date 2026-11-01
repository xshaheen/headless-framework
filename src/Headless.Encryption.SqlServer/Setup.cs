// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Encryption.SqlServer;
using Headless.Hosting.Schema;
using Headless.Sql;
using Headless.Sql.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Encryption;

/// <summary>Chooses SQL Server as the encryption key store.</summary>
[PublicAPI]
public static class SetupEncryptionSqlServer
{
    extension(HeadlessEncryptionSetupBuilder setup)
    {
        /// <summary>
        /// Stores the wrapped data keys in the database of the connection registered by <c>AddSqlServerSql</c>, so the keys
        /// live beside the data they protect.
        /// </summary>
        /// <returns>The setup builder for chaining.</returns>
        /// <remarks>
        /// Options resolution throws <see cref="InvalidOperationException"/> when <c>AddSqlServerSql</c> was not called or
        /// registered another provider's connection.
        /// </remarks>
        public HeadlessEncryptionSetupBuilder UseSqlServer()
        {
            return setup.UseSqlServer(
                (options, services) => options.ConnectionString = services.GetSqlServerConnectionString()
            );
        }

        /// <summary>Stores the wrapped data keys in SQL Server, in the database named by <paramref name="connectionString" />.</summary>
        /// <param name="connectionString">The SqlClient connection string.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>The key table is created by <c>resources provision</c> and verified at host start.</remarks>
        /// <exception cref="ArgumentException"><paramref name="connectionString" /> is <see langword="null" /> or whitespace.</exception>
        public HeadlessEncryptionSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Stores the wrapped data keys in SQL Server, binding <see cref="SqlServerEncryptionKeyStoreOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section bound to the store options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>The key table is created by <c>resources provision</c> and verified at host start.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessEncryptionSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterKeyStore(new SqlServerKeyStoreOptionsExtension(configuration));

            return setup;
        }

        /// <summary>Stores the wrapped data keys in SQL Server, configured by <paramref name="configure" />.</summary>
        /// <param name="configure">Delegate that configures the store options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>The key table is created by <c>resources provision</c> and verified at host start.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessEncryptionSetupBuilder UseSqlServer(Action<SqlServerEncryptionKeyStoreOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterKeyStore(new SqlServerKeyStoreOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Stores the wrapped data keys in SQL Server, configured by <paramref name="configure" /> with access to the
        /// application services.
        /// </summary>
        /// <param name="configure">Delegate that configures the store options with service resolution.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>The key table is created by <c>resources provision</c> and verified at host start.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessEncryptionSetupBuilder UseSqlServer(
            Action<SqlServerEncryptionKeyStoreOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterKeyStore(new SqlServerKeyStoreOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class SqlServerKeyStoreOptionsExtension : IEncryptionProviderOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<SqlServerEncryptionKeyStoreOptions>? _configure;
        private readonly Action<SqlServerEncryptionKeyStoreOptions, IServiceProvider>? _configureWithServices;

        public SqlServerKeyStoreOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public SqlServerKeyStoreOptionsExtension(Action<SqlServerEncryptionKeyStoreOptions> configure)
        {
            _configure = configure;
        }

        public SqlServerKeyStoreOptionsExtension(Action<SqlServerEncryptionKeyStoreOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<SqlServerEncryptionKeyStoreOptions, SqlServerEncryptionKeyStoreOptionsValidator>(
                    _configuration
                );
            }
            else if (_configure is not null)
            {
                services.Configure<SqlServerEncryptionKeyStoreOptions, SqlServerEncryptionKeyStoreOptionsValidator>(
                    _configure
                );
            }
            else
            {
                services.Configure<SqlServerEncryptionKeyStoreOptions, SqlServerEncryptionKeyStoreOptionsValidator>(
                    _configureWithServices
                );
            }

            // The contribution factory reads options at first resolution, so configured names reach the steps.
            services.AddHeadlessSchemaContribution(sp =>
                SqlServerEncryptionSchemaContribution.Create(
                    sp.GetRequiredService<IOptions<SqlServerEncryptionKeyStoreOptions>>().Value
                )
            );
            services.TryAddSingleton<IDataKeyStore>(sp => new RelationalDataKeyStore(
                SqlServerDialect.Instance,
                sp.GetRequiredService<IOptions<SqlServerEncryptionKeyStoreOptions>>().Value,
                sp.GetRequiredService<TimeProvider>()
            ));
        }
    }
}
