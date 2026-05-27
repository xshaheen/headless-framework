// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Abstractions;
using Headless.AuditLog;
using Headless.AuditLog.PostgreSql;
using Headless.Checks;
using Headless.Storage;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupAuditLogPostgreSql
{
    extension(HeadlessAuditLogSetupBuilder setup)
    {
        public HeadlessAuditLogSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(options =>
            {
                options.ConnectionString = connectionString;
            });
        }

        public HeadlessAuditLogSetupBuilder UsePostgreSql(Action<PostgreSqlAuditLogOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PostgreSqlAuditLogOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PostgreSqlAuditLogOptionsExtension(Action<PostgreSqlAuditLogOptions> configure)
        : IAuditLogStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.Configure<PostgreSqlAuditLogOptions, PostgreSqlAuditLogOptionsValidator>(configure);
            services.AddOptions<AuditLogStorageOptions, PostgreSqlAuditLogStorageOptionsValidator>();
            services.AddInitializerHostedService<PostgreSqlAuditLogStorageInitializer>();
            services.TryAddSingleton<PostgreSqlAuditLogWriter>();
            services.TryAddScoped<IAuditLogStore, PostgreSqlAuditLogStore>();
            services.TryAddSingleton(typeof(IAuditLog<>), typeof(PostgreSqlAuditLog<>));
            services.TryAddSingleton(typeof(IReadAuditLog<>), typeof(PostgreSqlReadAuditLog<>));
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IClock, Clock>();
            services.TryAddSingleton<ICurrentTenant, NullCurrentTenant>();
            services.TryAddSingleton<ICurrentUser, NullCurrentUser>();
            services.TryAddSingleton<ICorrelationIdProvider, ActivityCorrelationIdProvider>();
        }
    }

    private sealed class PostgreSqlAuditLogStorageOptionsValidator : AbstractValidator<AuditLogStorageOptions>
    {
        public PostgreSqlAuditLogStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
            RuleFor(x => x.TableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
            // PG accepts Jsonb (default) or Json; NvarcharMax is a SqlServer column type.
            RuleFor(x => x.JsonColumnType!.Value)
                .Must(t => t is AuditLogJsonColumnType.Jsonb or AuditLogJsonColumnType.Json)
                .WithMessage($"{nameof(AuditLogStorageOptions.JsonColumnType)} must be Jsonb or Json for the PostgreSql audit-log provider.")
                .When(x => x.JsonColumnType.HasValue);
            RuleFor(x => x.CreatedAtColumnType!)
                .MaximumLength(64)
                .Matches(@"^[A-Za-z][A-Za-z0-9 ]*(\([0-9]+\))?$")
                .When(x => !string.IsNullOrEmpty(x.CreatedAtColumnType));
        }
    }
}
