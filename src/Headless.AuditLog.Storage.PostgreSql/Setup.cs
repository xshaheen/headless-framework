// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Abstractions;
using Headless.AuditLog.PostgreSql;
using Headless.Checks;
using Headless.Serializer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.AuditLog;

[PublicAPI]
public static class SetupAuditLogPostgreSql
{
    extension(HeadlessAuditLogSetupBuilder setup)
    {
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

            services.AddOptions<AuditLogStorageOptions, PostgreSqlAuditLogStorageOptionsValidator>();
            services.AddInitializerHostedService<PostgreSqlAuditLogStorageInitializer>();
            services.TryAddSingleton<IJsonSerializer>(_ => new SystemJsonSerializer());
            services.TryAddSingleton<PostgreSqlAuditLogWriter>();
            services.TryAddScoped<IAuditLogStore, PostgreSqlAuditLogStore>();
            services.TryAddSingleton(typeof(IAuditLog<>), typeof(PostgreSqlAuditLog<>));
            services.TryAddSingleton(typeof(IReadAuditLog<>), typeof(PostgreSqlReadAuditLog<>));
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ICurrentTenant, NullCurrentTenant>();
            services.TryAddSingleton<ICurrentUser, NullCurrentUser>();
            services.TryAddSingleton<ICorrelationIdProvider, ActivityCorrelationIdProvider>();
        }
    }

    private sealed class PostgreSqlAuditLogStorageOptionsValidator : AbstractValidator<AuditLogStorageOptions>
    {
        public PostgreSqlAuditLogStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidPostgreSqlIdentifier();
            RuleFor(x => x.TableName).IsValidPostgreSqlIdentifier();
            // PG accepts Jsonb (default) or Json; NvarcharMax is a SqlServer column type.
            When(
                x => x.JsonColumnType.HasValue,
                () =>
                {
                    RuleFor(x => x.JsonColumnType!.Value)
                        .Must(t => t is AuditLogJsonColumnType.Jsonb or AuditLogJsonColumnType.Json)
                        .WithMessage(
                            $"{nameof(AuditLogStorageOptions.JsonColumnType)} must be Jsonb or Json for the PostgreSql audit-log provider."
                        );
                }
            );
            RuleFor(x => x.CreatedAtColumnType!)
                .MaximumLength(64)
                .Matches(@"^[A-Za-z][A-Za-z0-9 ]*(\([0-9]+\))?$")
                .When(x => !string.IsNullOrEmpty(x.CreatedAtColumnType));
        }
    }
}
