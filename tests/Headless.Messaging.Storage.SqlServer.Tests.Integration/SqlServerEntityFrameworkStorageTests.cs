// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Storage.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Runs the whole SQL Server storage suite on storage whose connection settings come from
/// <c>UseSqlServerEntityFramework&lt;TContext&gt;()</c>: the connection string EF Core holds for the context, rather
/// than settings the application passes to messaging.
/// </summary>
[Collection<SqlServerTestFixture>]
public sealed class SqlServerEntityFrameworkStorageTests(SqlServerTestFixture fixture) : SqlServerStorageTests(fixture)
{
    private ServiceProvider? _registration;

    /// <inheritdoc />
    protected override bool RegisteredFromEntityFrameworkContext => true;

    /// <inheritdoc />
    protected override IOptions<SqlServerOptions> ResolveStorageOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<StorageConformanceDbContext>(options => options.UseSqlServer(Fixture.ConnectionString));
        services.AddHeadlessMessaging(setup => setup.UseSqlServerEntityFramework<StorageConformanceDbContext>());
        _registration = services.BuildServiceProvider();

        return _registration.GetRequiredService<IOptions<SqlServerOptions>>();
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        await base.DisposeAsyncCore();

        if (_registration is not null)
        {
            await _registration.DisposeAsync();
        }
    }

    /// <summary>An application context that maps nothing of its own; messaging only borrows its connection.</summary>
    public sealed class StorageConformanceDbContext(DbContextOptions<StorageConformanceDbContext> options)
        : DbContext(options);
}
