// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Framework.Database.Sqlite;
using Framework.ResourceLocks.Storage.RegularLocks;
using Framework.ResourceLocks.Storage.ThrottlingLocks;

namespace Tests.TestSetup;

[CollectionDefinition(nameof(ResourceLockTestFixture))]
public sealed class ResourceLockTestFixtureCollection : ICollectionFixture<ResourceLockTestFixture>;

public sealed class ResourceLockTestFixture : IAsyncLifetime, IDisposable, IAsyncDisposable
{
    private readonly SqliteConnectionFactory _connectionFactory = new("DataSource=:memory:");

    public IResourceLockStorage ResourceLockStorage { get; private set; } = null!;

    public IThrottlingResourceLockStorage ThrottlingResourceLockStorage { get; private set; } = null!;

    /// <summary>This runs before all the test run and Called just after the constructor</summary>
    async Task IAsyncLifetime.InitializeAsync()
    {
        // Create the resource lock table
        var connection = await _connectionFactory.GetOpenConnectionAsync();
        _CreateResourceLockTable(connection);
        ResourceLockStorage = new SqliteResourceLockStorage(connection);
        _CreateThrottlingResourceLockTable(connection);
        ThrottlingResourceLockStorage = new SqliteThrottlingResourceLockStorage(connection);
    }

    /// <summary>This runs after all the test run and Called before Dispose()</summary>
    async Task IAsyncLifetime.DisposeAsync()
    {
        await _connectionFactory.DisposeAsync();
    }

    public void Dispose()
    {
        _connectionFactory.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionFactory.DisposeAsync();
    }

    private static void _CreateResourceLockTable(IDbConnection connection)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            CREATE TABLE ResourceLocks (
                Key TEXT PRIMARY KEY,
                Value TEXT,
                Expiration TEXT
            )
            """;

        command.ExecuteNonQuery();
    }

    private static void _CreateThrottlingResourceLockTable(IDbConnection connection)
    {
        using var command = connection.CreateCommand();

        command.CommandText = """
            CREATE TABLE ThrottlingResourceLocks (
                Key TEXT PRIMARY KEY,
                Value INTEGER,
                Expiration TEXT
            )
            """;

        command.ExecuteNonQuery();
    }
}
