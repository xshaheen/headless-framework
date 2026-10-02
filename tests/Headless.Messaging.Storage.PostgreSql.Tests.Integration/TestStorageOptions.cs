// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Hosting.Initialization.Schema;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.PostgreSql;
using Headless.Sql.PostgreSql;
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
        return new RelationalStorageTableNames(PostgreSqlDialect.Instance, For(schema));
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
        return ApplyAsync(new PostgreSqlOptions { ConnectionString = connectionString }, schema, cancellationToken);
    }

    public static Task ApplyAsync(
        PostgreSqlOptions options,
        string schema = HeadlessStorageDefaults.Schema,
        CancellationToken cancellationToken = default
    )
    {
        return Contribution(options, schema).ApplyAsync(cancellationToken);
    }

    public static SchemaContribution Contribution(
        PostgreSqlOptions options,
        string schema = HeadlessStorageDefaults.Schema
    )
    {
        return PostgreSqlMessagingSchemaContribution.Create(options, new MessagingStorageOptions { Schema = schema });
    }

    /// <summary>
    /// Registers the table names and the schema contribution on a hand-built service collection, so
    /// <c>ApplyMessagingSchemaAsync</c> finds a runner. Reads the bound <see cref="PostgreSqlOptions"/> and
    /// <see cref="MessagingStorageOptions"/>.
    /// </summary>
    public static IServiceCollection AddTestMessagingSchema(this IServiceCollection services)
    {
        services.AddSingleton<IStorageTableNames>(sp => new RelationalStorageTableNames(
            PostgreSqlDialect.Instance,
            sp.GetRequiredService<IOptions<MessagingStorageOptions>>()
        ));
        services.AddHeadlessSchemaContribution(sp =>
            PostgreSqlMessagingSchemaContribution.Create(
                sp.GetRequiredService<IOptions<PostgreSqlOptions>>().Value,
                sp.GetRequiredService<IOptions<MessagingStorageOptions>>().Value
            )
        );

        return services;
    }
}
