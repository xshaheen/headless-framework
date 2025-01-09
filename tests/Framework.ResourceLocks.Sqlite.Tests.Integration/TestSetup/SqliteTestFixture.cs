// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Database.Sqlite;
using Framework.ResourceLocks;
using Framework.ResourceLocks.Sqlite;
using Framework.ResourceLocks.Storage.RegularLocks;

namespace Tests.TestSetup;

[CollectionDefinition(nameof(SqliteTestFixture))]
public sealed class SqliteTestFixture : IAsyncLifetime, IAsyncDisposable, ICollectionFixture<SqliteTestFixture>
{
    private readonly SqliteConnectionFactory _connectionFactory = new("DataSource=./ThrottlingResourceLocks.db");

    public IResourceLockStorage ResourceLockStorage { get; private set; } = null!;

    public IThrottlingResourceLockStorage ThrottlingResourceLockStorage { get; private set; } = null!;

    /// <summary>This runs before all the test run and Called just after the constructor</summary>
    async Task IAsyncLifetime.InitializeAsync()
    {
        // Create the resource lock table
        var connection = await _connectionFactory.GetOpenConnectionAsync();
        var resourceLockStorage = new SqliteResourceLockStorage(connection);
        await resourceLockStorage.CreateTableAsync();
        ResourceLockStorage = resourceLockStorage;

        var throttlingResourceLockStorage = new SqliteThrottlingResourceLockStorage(connection, TimeProvider.System);
        await throttlingResourceLockStorage.CreateTableAsync();
        ThrottlingResourceLockStorage = throttlingResourceLockStorage;
    }

    /// <summary>This runs after all the test run and Called before Dispose()</summary>
    Task IAsyncLifetime.DisposeAsync()
    {
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionFactory.DisposeAsync();
    }
}
