// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>A database whose schema EF Core created from the <see cref="LiveOrdersDb" /> model.</summary>
public interface ILiveDatabase
{
    /// <summary>Points a context at this database with the provider's own connections, one per context.</summary>
    void Configure(DbContextOptionsBuilder options);

    /// <summary>The schema-qualified orders table as this provider writes it in SQL.</summary>
    string OrdersTableSql { get; }
}

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlLiveFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlLiveFixture>,
        ILiveDatabase
{
    public string OrdersTableSql => "\"sales\".\"Orders\"";

    public void Configure(DbContextOptionsBuilder options) => options.UseNpgsql(ConnectionString);

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await LiveDatabaseSchema.CreateAsync(this);
    }
}

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerLiveFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerLiveFixture>,
        ILiveDatabase
{
    public string OrdersTableSql => "[sales].[Orders]";

    public void Configure(DbContextOptionsBuilder options) => options.UseSqlServer(ConnectionString);

    // The base creates the fixture's own empty database; the model's schema and tables are added to it.
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await LiveDatabaseSchema.CreateAsync(this);
    }
}

internal static class LiveDatabaseSchema
{
    public static async Task CreateAsync(ILiveDatabase database)
    {
        var options = new DbContextOptionsBuilder<LiveOrdersDb>();
        database.Configure(options);
        await using var context = new LiveOrdersDb(options.Options);
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
    }
}
