// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>A database whose schema EF Core created from a <see cref="WorkContext" /> model.</summary>
public interface IWorkDatabase
{
    WorkContext CreateContext();
}

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlWorkFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlWorkFixture>,
        IWorkDatabase
{
    public WorkContext CreateContext()
    {
        return new PostgreSqlWorkContext(
            new DbContextOptionsBuilder<PostgreSqlWorkContext>().UseNpgsql(ConnectionString).Options
        );
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
    }
}

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerWorkFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerWorkFixture>,
        IWorkDatabase
{
    public WorkContext CreateContext()
    {
        return new SqlServerWorkContext(
            new DbContextOptionsBuilder<SqlServerWorkContext>().UseSqlServer(ConnectionString).Options
        );
    }

    // The base creates the fixture's own empty database; EnsureCreated adds the model's tables to it.
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
    }
}

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqliteWorkFixture : IAsyncLifetime, ICollectionFixture<SqliteWorkFixture>, IWorkDatabase
{
    private SqliteTestDatabase? _database;

    public WorkContext CreateContext()
    {
        return new SqliteWorkContext(
            new DbContextOptionsBuilder<SqliteWorkContext>().UseSqlite(_database!.ConnectionString).Options
        );
    }

    public async ValueTask InitializeAsync()
    {
        _database = SqliteTestDatabase.Create();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }
}
