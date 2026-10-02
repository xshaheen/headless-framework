// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Abstractions;
using Headless.Constants;
using Headless.Hosting.Initialization.Schema;
using Headless.MultiTenancy;
using Headless.Serializer;
using Headless.Sql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog;

/// <summary>How one relational provider stores the audit log, beyond what its dialect already says.</summary>
/// <param name="Provider">The database whose identifier rules the storage options must satisfy.</param>
/// <param name="DefaultJsonColumnType">The JSON column type used when none is configured.</param>
/// <param name="JsonColumnTypes">The JSON column types the provider accepts.</param>
/// <param name="JsonColumnTypeMessage">The validation message for any other JSON column type.</param>
/// <param name="CreateCreatedAtBinder">
/// Builds the <c>CreatedAt</c> binder for the configured column type, or <see langword="null"/> to bind every instant as
/// the dialect's timestamp.
/// </param>
internal sealed record RelationalAuditLogProvider(
    StorageProvider Provider,
    AuditLogJsonColumnType DefaultJsonColumnType,
    IReadOnlyCollection<AuditLogJsonColumnType> JsonColumnTypes,
    string JsonColumnTypeMessage,
    Func<AuditLogStorageOptions, AuditLogCreatedAtBinder>? CreateCreatedAtBinder = null
);

/// <summary>Registers the relational audit log, its store and reader, and a provider's schema contribution.</summary>
internal static class RelationalAuditLogStorage
{
    public static void AddServices<TOptions>(
        IServiceCollection services,
        ISqlDialect dialect,
        RelationalAuditLogProvider provider,
        Func<TOptions, RelationalAuditLogTable, AuditLogStorageOptions, SchemaContribution> createContribution
    )
        where TOptions : RelationalAuditLogOptions
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidator<AuditLogStorageOptions>>(
                new RelationalAuditLogStorageOptionsValidator(
                    provider.Provider,
                    provider.JsonColumnTypes,
                    provider.JsonColumnTypeMessage
                )
            )
        );
        services.AddOptions<AuditLogStorageOptions>().ValidateFluentValidation().ValidateOnStart();
        services.TryAddSingleton(sp =>
        {
            var storageOptions = sp.GetRequiredService<IOptions<AuditLogStorageOptions>>().Value;

            return new RelationalAuditLogTable(
                dialect,
                sp.GetRequiredService<IOptions<TOptions>>().Value,
                storageOptions,
                provider.DefaultJsonColumnType,
                provider.CreateCreatedAtBinder?.Invoke(storageOptions)
            );
        });

        // The contribution factory reads options at first resolution, so InitializeOnStartup keeps its contract:
        // false keeps the steps out of the runner's apply pass while verify and export still see them.
        services.AddHeadlessSchemaContribution(sp =>
            createContribution(
                sp.GetRequiredService<IOptions<TOptions>>().Value,
                sp.GetRequiredService<RelationalAuditLogTable>(),
                sp.GetRequiredService<IOptions<AuditLogStorageOptions>>().Value
            )
        );

        services.TryAddSingleton<IJsonSerializer>(_ => new SystemJsonSerializer());
        services.TryAddSingleton<RelationalAuditLogWriter>();
        services.TryAddScoped<IAuditLogStore, RelationalAuditLogStore>();
        services.TryAddSingleton(typeof(IAuditLog<>), typeof(RelationalAuditLog<>));
        services.TryAddSingleton(typeof(IAuditLogWriter<>), typeof(RelationalAuditLog<>));
        services.TryAddSingleton(typeof(IReadAuditLog<>), typeof(RelationalReadAuditLog<>));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ICurrentTenant, NullCurrentTenant>();
        services.TryAddSingleton<ICurrentUser, NullCurrentUser>();
        services.TryAddSingleton<ICorrelationIdProvider, ActivityCorrelationIdProvider>();
    }
}
