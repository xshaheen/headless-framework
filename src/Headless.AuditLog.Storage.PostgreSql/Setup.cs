// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog.PostgreSql;
using Headless.Checks;
using Headless.Constants;
using Headless.Sql;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.AuditLog;

[PublicAPI]
public static class SetupAuditLogPostgreSql
{
    extension(HeadlessAuditLogSetupBuilder setup)
    {
        /// <summary>
        /// Configures PostgreSQL storage with the connection registered by <c>AddPostgreSqlSql</c>, so one
        /// connection string serves every feature that shares the database.
        /// </summary>
        /// <returns>The setup builder for chaining.</returns>
        /// <remarks>
        /// Options resolution throws <see cref="InvalidOperationException"/> when <c>AddPostgreSqlSql</c> was not
        /// called or registered another provider's connection.
        /// </remarks>
        public HeadlessAuditLogSetupBuilder UsePostgreSql()
        {
            return setup.UsePostgreSql(
                (options, services) => options.ConnectionString = services.GetPostgreSqlConnectionString()
            );
        }

        /// <summary>
        /// Configures the audit log to persist entries to PostgreSql using the provided
        /// connection string.
        /// </summary>
        /// <param name="connectionString">Npgsql connection string. Must not be <see langword="null"/> or whitespace.</param>
        /// <exception cref="ArgumentException"><paramref name="connectionString"/> is <see langword="null"/> or whitespace.</exception>
        public HeadlessAuditLogSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options => options.ConnectionString = connectionString);
        }

        /// <summary>
        /// Configures the audit log to persist entries to PostgreSql, binding
        /// <see cref="PostgreSqlAuditLogOptions"/> from the specified <paramref name="configuration"/>.
        /// </summary>
        /// <param name="configuration">Configuration section to bind to <see cref="PostgreSqlAuditLogOptions"/>. Must not be <see langword="null"/>.</param>
        /// <remarks>
        /// The provider self-initializes the schema and table on startup (serialized with
        /// <c>pg_advisory_xact_lock</c> across replicas) unless
        /// <see cref="AuditLogStorageOptions.InitializeOnStartup"/> is <see langword="false"/>. Audit rows
        /// are written via batched <c>INSERT … VALUES</c> statements (up to 500 rows per command).
        /// When an <see cref="IAmbientDbTransactionAccessor"/> is registered and the calling
        /// <c>DbContext</c> has an open <c>NpgsqlTransaction</c>, writes enroll atomically in
        /// that transaction; otherwise they commit on a separate connection.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessAuditLogSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(new PostgreSqlAuditLogOptionsExtension(configuration));

            return setup;
        }

        /// <summary>
        /// Configures the audit log to persist entries to PostgreSql, applying the specified
        /// options delegate to <see cref="PostgreSqlAuditLogOptions"/>.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options.</param>
        /// <remarks>
        /// The provider self-initializes the schema and table on startup (serialized with
        /// <c>pg_advisory_xact_lock</c> across replicas) unless
        /// <see cref="AuditLogStorageOptions.InitializeOnStartup"/> is <see langword="false"/>. Audit rows
        /// are written via batched <c>INSERT … VALUES</c> statements (up to 500 rows per command).
        /// When an <see cref="IAmbientDbTransactionAccessor"/> is registered and the calling
        /// <c>DbContext</c> has an open <c>NpgsqlTransaction</c>, writes enroll atomically in
        /// that transaction; otherwise they commit on a separate connection.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessAuditLogSetupBuilder UsePostgreSql(Action<PostgreSqlAuditLogOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlAuditLogOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Configures the audit log to persist entries to PostgreSql, applying the specified
        /// options delegate to <see cref="PostgreSqlAuditLogOptions"/> with access to the
        /// resolved <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="configure">Delegate that configures the provider options with service resolution.</param>
        /// <remarks>
        /// The provider self-initializes the schema and table on startup (serialized with
        /// <c>pg_advisory_xact_lock</c> across replicas) unless
        /// <see cref="AuditLogStorageOptions.InitializeOnStartup"/> is <see langword="false"/>. Audit rows
        /// are written via batched <c>INSERT … VALUES</c> statements (up to 500 rows per command).
        /// When an <see cref="IAmbientDbTransactionAccessor"/> is registered and the calling
        /// <c>DbContext</c> has an open <c>NpgsqlTransaction</c>, writes enroll atomically in
        /// that transaction; otherwise they commit on a separate connection.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessAuditLogSetupBuilder UsePostgreSql(Action<PostgreSqlAuditLogOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlAuditLogOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PostgreSqlAuditLogOptionsExtension : IAuditLogStorageOptionsExtension
    {
        private readonly IConfiguration? _configuration;
        private readonly Action<PostgreSqlAuditLogOptions>? _configure;
        private readonly Action<PostgreSqlAuditLogOptions, IServiceProvider>? _configureWithServices;

        public PostgreSqlAuditLogOptionsExtension(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public PostgreSqlAuditLogOptionsExtension(Action<PostgreSqlAuditLogOptions> configure)
        {
            _configure = configure;
        }

        public PostgreSqlAuditLogOptionsExtension(Action<PostgreSqlAuditLogOptions, IServiceProvider> configure)
        {
            _configureWithServices = configure;
        }

        public void AddServices(IServiceCollection services)
        {
            if (_configuration is not null)
            {
                services.Configure<PostgreSqlAuditLogOptions, PostgreSqlAuditLogOptionsValidator>(_configuration);
            }
            else if (_configure is not null)
            {
                services.Configure<PostgreSqlAuditLogOptions, PostgreSqlAuditLogOptionsValidator>(_configure);
            }
            else
            {
                services.Configure<PostgreSqlAuditLogOptions, PostgreSqlAuditLogOptionsValidator>(
                    _configureWithServices
                );
            }

            RelationalAuditLogStorage.AddServices<PostgreSqlAuditLogOptions>(
                services,
                PostgreSqlDialect.Instance,
                new RelationalAuditLogProvider(
                    StorageProvider.PostgreSql,
                    AuditLogJsonColumnType.Jsonb,
                    [AuditLogJsonColumnType.Jsonb, AuditLogJsonColumnType.Json],
                    $"{nameof(AuditLogStorageOptions.JsonColumnType)} must be Jsonb or Json for the PostgreSql audit-log provider."
                ),
                PostgreSqlAuditLogSchemaContribution.Create
            );
        }
    }
}
