// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Hosting.Initialization;
using Headless.Hosting.Initialization.Schema;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlDistributedLockFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlDistributedLockFixture>
{
    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure()
            .WithDatabase("distributed_locks_test")
            .WithUsername("postgres")
            .WithPassword("postgres");
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        // The container is reused across runs, and the schema runner trusts its history, so start from neither the
        // fence sequence nor the history that records it.
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync(CancellationToken.None);
            await using var reset = new NpgsqlCommand(
                $"""DROP SCHEMA IF EXISTS "{HeadlessStorageDefaults.Schema}" CASCADE;""",
                connection
            );
            await reset.ExecuteNonQueryAsync(CancellationToken.None);
        }

        // A host creates the fence sequence at startup; the tests resolve providers without a host, so the fixture
        // runs that startup once for the default schema they all share.
        await ApplySchemaAsync(ConnectionString, schema: null, CancellationToken.None);
    }

    /// <summary>
    /// Runs the schema runner for a DistributedLocks registration against <paramref name="connectionString"/>, as a
    /// host would at startup, in <paramref name="schema"/> or the default schema when it is <see langword="null"/>.
    /// </summary>
    public static async Task ApplySchemaAsync(
        string connectionString,
        string? schema,
        CancellationToken cancellationToken
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
        {
            if (schema is not null)
            {
                setup.ConfigureStorage(storage => storage.Schema = schema);
            }

            setup.UsePostgreSql(connectionString);
        });

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<SchemaRunner>().ApplyAsync(cancellationToken);
    }
}
