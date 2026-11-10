// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Search;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Tests;

/// <summary>
/// One PostgreSQL container. The parity suite shares one database whose function is provisioned once; tests that
/// prove provisioning or migration order each create a database of their own, so they start without the function.
/// </summary>
[UsedImplicitly]
[CollectionDefinition]
public sealed class PostgreSqlSearchFixture : HeadlessPostgreSqlFixture, ICollectionFixture<PostgreSqlSearchFixture>
{
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        using var host = CreateHost(ConnectionString);
        await host.Services.ProvisionResourcesAsync(CancellationToken.None);
    }

    /// <summary>Creates an empty database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken, string? lcCtype = null)
    {
        var name = DerivedDatabaseName($"t_{Guid.NewGuid():N}");

        if (lcCtype is null)
        {
            await CreateDatabaseAsync(name, cancellationToken);

            return ConnectionStringFor(name);
        }

        // The base helper creates a database with the server's default locale; a derived name still lets the
        // fixture drop this one on dispose.
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var create = new NpgsqlCommand(
            $"""CREATE DATABASE "{name}" TEMPLATE template0 LC_CTYPE '{lcCtype}' LC_COLLATE '{lcCtype}';""",
            connection
        );
        await create.ExecuteNonQueryAsync(cancellationToken);

        return ConnectionStringFor(name);
    }

    /// <summary>A host that registers PostgreSQL search on <paramref name="connectionString" />.</summary>
    public static IHost CreateHost(string connectionString, Action<IServiceCollection>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessSearch(setup => setup.UsePostgreSql(connectionString));
        configure?.Invoke(builder.Services);

        return builder.Build();
    }
}
