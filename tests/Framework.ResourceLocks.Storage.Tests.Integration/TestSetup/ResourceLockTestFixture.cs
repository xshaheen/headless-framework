// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Database.Sqlite;
using Framework.ResourceLocks;
using Framework.ResourceLocks.Storage.RegularLocks;

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

        var resourceLockStorage = new SqliteResourceLockStorage(connection);
        resourceLockStorage.CreateTable();
        ResourceLockStorage = resourceLockStorage;

        var throttlingResourceLockStorage = new SqliteThrottlingResourceLockStorage(connection);
        throttlingResourceLockStorage.CreateTable();
        ThrottlingResourceLockStorage = throttlingResourceLockStorage;
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
}
