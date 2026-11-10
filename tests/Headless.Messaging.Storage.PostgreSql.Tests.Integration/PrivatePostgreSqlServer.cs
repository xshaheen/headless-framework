// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Npgsql;

namespace Tests;

/// <summary>A PostgreSQL server one test starts, owns, and disposes, outside the collection fixture.</summary>
internal sealed class PrivatePostgreSqlServer : HeadlessPostgreSqlFixture
{
    // Its tests stop the server or create server-wide roles, which would disturb every other fixture on a shared one.
    protected override bool ShareContainer => false;

    public ValueTask StartAsync() => InitializeAsync();

    protected override async ValueTask DisposeAsyncCore()
    {
        try
        {
            await base.DisposeAsyncCore();
        }
        catch (NpgsqlException)
        {
            // A test that left the server stopped keeps its database from being dropped; removing the container does it.
            await Container.DisposeAsync();
        }
    }
}
