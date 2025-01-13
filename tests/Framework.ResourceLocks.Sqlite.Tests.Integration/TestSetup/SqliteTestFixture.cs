// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Database.Sqlite;
using Framework.ResourceLocks.Sqlite;
using Microsoft.Data.Sqlite;

namespace Tests.TestSetup;

[CollectionDefinition(nameof(SqliteTestFixture), DisableParallelization = false)]
public sealed class SqliteTestFixture : IAsyncLifetime, IAsyncDisposable, ICollectionFixture<SqliteTestFixture>
{
    public SqliteConnectionFactory ConnectionFactory { get; } = new("DataSource=./ThrottlingResourceLocks.db");

    public SqliteConnection? Connection { get; private set; }

    public SqliteResourceLockStorage LockStorage { get; private set; } = null!;

    public SqliteThrottlingResourceLockStorage ThrottlingLockStorage { get; private set; } = null!;

    /// <summary>This runs before all the test run and Called just after the constructor</summary>
    async Task IAsyncLifetime.InitializeAsync()
    {
        // Create the resource lock table
        Connection = await ConnectionFactory.GetOpenConnectionAsync();

        LockStorage = new SqliteResourceLockStorage(Connection);
        await LockStorage.CreateTableAsync();

        ThrottlingLockStorage = new(Connection, TimeProvider.System);
        await ThrottlingLockStorage.CreateTableAsync();
    }

    /// <summary>This runs after all the test run and Called before Dispose()</summary>
    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    public async ValueTask DisposeAsync() => await ConnectionFactory.DisposeAsync();
}
