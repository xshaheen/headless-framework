// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Hosting.Initialization.Schema;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.SqlServer;
using Headless.Sql.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Builds the feature-owned storage options these tests hand to a directly constructed storage,
/// standing in for the DI registration the setup builder would normally make.
/// </summary>
internal static class TestStorageOptions
{
    public static IOptions<MessagingStorageOptions> For(string schema = HeadlessStorageDefaults.Schema)
    {
        return Options.Create(new MessagingStorageOptions { Schema = schema });
    }

    public static IStorageTableNames TableNames(string schema = HeadlessStorageDefaults.Schema)
    {
        return new RelationalStorageTableNames(SqlServerDialect.Instance, For(schema));
    }
}

/// <summary>
/// Applies the messaging schema contribution for tests that construct the storage by hand instead of starting a
/// host, where the hosted schema runner would apply it.
/// </summary>
internal static class TestMessagingSchema
{
    public static Task ApplyAsync(
        string connectionString,
        string schema = HeadlessStorageDefaults.Schema,
        CancellationToken cancellationToken = default
    )
    {
        return ApplyAsync(new SqlServerOptions { ConnectionString = connectionString }, schema, cancellationToken);
    }

    public static Task ApplyAsync(
        SqlServerOptions options,
        string schema = HeadlessStorageDefaults.Schema,
        CancellationToken cancellationToken = default
    )
    {
        return Contribution(options, schema).ApplyAsync(cancellationToken);
    }

    public static SchemaContribution Contribution(
        SqlServerOptions options,
        string schema = HeadlessStorageDefaults.Schema
    )
    {
        return SqlServerMessagingSchemaContribution.Create(options, new MessagingStorageOptions { Schema = schema });
    }

    /// <summary>
    /// Drops every messaging object in <paramref name="schema"/> together with the schema runner's history, and then
    /// the schema unless it is <c>dbo</c>. The history goes too: the runner trusts it, so a history that outlived the
    /// tables would stop the next apply recreating them.
    /// </summary>
    public static string DropSql(string schema)
    {
        return $"""
            DROP TABLE IF EXISTS [{schema}].MessagingInboxAudit;
            DROP TABLE IF EXISTS [{schema}].MessagingInboxOperationReceipts;
            DROP TABLE IF EXISTS [{schema}].MessagingPublished;
            DROP TABLE IF EXISTS [{schema}].MessagingReceived;
            DROP TABLE IF EXISTS [{schema}].[{SchemaRunner.HistoryTableName}];
            -- The schema no longer creates table types; these clear the ones a reused container kept from an older
            -- binary, which would otherwise block DROP SCHEMA.
            DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingIdList];
            DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingOwnerList];
            DROP TYPE IF EXISTS [{schema}].[HeadlessMessagingPoisonMessageList];
            IF N'{schema}' <> N'dbo' AND SCHEMA_ID(N'{schema}') IS NOT NULL EXEC(N'DROP SCHEMA [{schema}]');
            """;
    }

    /// <summary>
    /// Registers the table names and the schema contribution on a hand-built service collection, so
    /// <c>ApplyMessagingSchemaAsync</c> finds a runner. Reads the bound <see cref="SqlServerOptions"/> and
    /// <see cref="MessagingStorageOptions"/>.
    /// </summary>
    public static IServiceCollection AddTestMessagingSchema(this IServiceCollection services)
    {
        services.AddSingleton<IStorageTableNames>(sp => new RelationalStorageTableNames(
            SqlServerDialect.Instance,
            sp.GetRequiredService<IOptions<MessagingStorageOptions>>()
        ));
        services.AddHeadlessSchemaContribution(sp =>
            SqlServerMessagingSchemaContribution.Create(
                sp.GetRequiredService<IOptions<SqlServerOptions>>().Value,
                sp.GetRequiredService<IOptions<MessagingStorageOptions>>().Value
            )
        );

        return services;
    }
}
