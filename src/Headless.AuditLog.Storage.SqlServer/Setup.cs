// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog.SqlServer;
using Headless.Checks;
using Headless.Sql;
using Headless.Sql.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.AuditLog;

[PublicAPI]
public static class SetupAuditLogSqlServer
{
    extension(HeadlessAuditLogSetupBuilder setup)
    {
        /// <summary>
        /// Configures SQL Server storage with the connection registered by <c>AddSqlServerSql</c>, so one
        /// connection string serves every feature that shares the database.
        /// </summary>
        /// <returns>The setup builder for chaining.</returns>
        /// <remarks>
        /// Options resolution throws <see cref="InvalidOperationException"/> when <c>AddSqlServerSql</c> was not
        /// called or registered another provider's connection.
        /// </remarks>
        public HeadlessAuditLogSetupBuilder UseSqlServer()
        {
            return setup.UseSqlServer(
                (options, services) => options.ConnectionString = services.GetSqlServerConnectionString()
            );
        }

        /// <summary>
        /// Configures the audit log to persist entries to SQL Server using the provided
        /// connection string.
        /// </summary>
        /// <param name="connectionString">Microsoft.Data.SqlClient connection string. Must not be <see langword="null"/> or whitespace.</param>
        /// <exception cref="ArgumentException"><paramref name="connectionString"/> is <see langword="null"/> or whitespace.</exception>
        public HeadlessAuditLogSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Configures the audit log to persist entries to SQL Server, binding
        /// <see cref="SqlServerAuditLogOptions"/> from the specified <paramref name="configuration"/>.
        /// </summary>
        /// <param name="configuration">Configuration section to bind to <see cref="SqlServerAuditLogOptions"/>. Must not be <see langword="null"/>.</param>
        /// <remarks>
        /// The provider self-initializes the schema and table on startup (serialized with
        /// <c>sp_getapplock</c> across replicas) unless
        /// <see cref="AuditLogStorageOptions.InitializeOnStartup"/> is <see langword="false"/>. Audit rows
        /// are written via batched <c>INSERT … VALUES</c> statements (up to 100 rows per command).
        /// When an <see cref="IAmbientDbTransactionAccessor"/> is registered and the calling
        /// <c>DbContext</c> has an open <c>SqlTransaction</c>, writes enroll atomically in
        /// that transaction; otherwise <see cref="AuditLogOptions.MissingTransactionStrategy"/> decides whether
        /// they commit on a separate connection or throw.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessAuditLogSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new SqlServerAuditLogOptionsExtension(configuration));

            return setup;
        }

        /// <summary>
        /// Configures the audit log to persist entries to SQL Server, applying the specified
        /// options delegate to <see cref="SqlServerAuditLogOptions"/>.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options.</param>
        /// <remarks>
        /// The provider self-initializes the schema and table on startup (serialized with
        /// <c>sp_getapplock</c> across replicas) unless
        /// <see cref="AuditLogStorageOptions.InitializeOnStartup"/> is <see langword="false"/>. Audit rows
        /// are written via batched <c>INSERT … VALUES</c> statements (up to 100 rows per command).
        /// When an <see cref="IAmbientDbTransactionAccessor"/> is registered and the calling
        /// <c>DbContext</c> has an open <c>SqlTransaction</c>, writes enroll atomically in
        /// that transaction; otherwise <see cref="AuditLogOptions.MissingTransactionStrategy"/> decides whether
        /// they commit on a separate connection or throw.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessAuditLogSetupBuilder UseSqlServer(Action<SqlServerAuditLogOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerAuditLogOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Configures the audit log to persist entries to SQL Server, applying the specified
        /// options delegate to <see cref="SqlServerAuditLogOptions"/> with access to the
        /// resolved <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options with service resolution.</param>
        /// <remarks>
        /// The provider self-initializes the schema and table on startup (serialized with
        /// <c>sp_getapplock</c> across replicas) unless
        /// <see cref="AuditLogStorageOptions.InitializeOnStartup"/> is <see langword="false"/>. Audit rows
        /// are written via batched <c>INSERT … VALUES</c> statements (up to 100 rows per command).
        /// When an <see cref="IAmbientDbTransactionAccessor"/> is registered and the calling
        /// <c>DbContext</c> has an open <c>SqlTransaction</c>, writes enroll atomically in
        /// that transaction; otherwise <see cref="AuditLogOptions.MissingTransactionStrategy"/> decides whether
        /// they commit on a separate connection or throw.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessAuditLogSetupBuilder UseSqlServer(Action<SqlServerAuditLogOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new SqlServerAuditLogOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class SqlServerAuditLogOptionsExtension : IAuditLogStorageOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<SqlServerAuditLogOptions>? _configure;
        private readonly Action<SqlServerAuditLogOptions, IServiceProvider>? _configureWithServices;

        public SqlServerAuditLogOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public SqlServerAuditLogOptionsExtension(Action<SqlServerAuditLogOptions> configure)
        {
            _configure = configure;
        }

        public SqlServerAuditLogOptionsExtension(Action<SqlServerAuditLogOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<SqlServerAuditLogOptions, SqlServerAuditLogOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<SqlServerAuditLogOptions, SqlServerAuditLogOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<SqlServerAuditLogOptions, SqlServerAuditLogOptionsValidator>(_configureWithServices);
            }

            RelationalAuditLogStorage.AddServices<SqlServerAuditLogOptions>(
                services,
                SqlServerDialect.Instance,
                new RelationalAuditLogProvider(
                    StorageProvider.SqlServer,
                    AuditLogJsonColumnType.NvarcharMax,
                    [AuditLogJsonColumnType.NvarcharMax],
                    $"{nameof(AuditLogStorageOptions.JsonColumnType)} must be NvarcharMax for the SqlServer audit-log provider.",
                    SqlServerAuditLogSchemaContribution.CreatedAtBinder
                ),
                SqlServerAuditLogSchemaContribution.Create
            );
        }
    }
}
