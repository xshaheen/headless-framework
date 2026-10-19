// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Storage.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Runs the whole PostgreSQL storage suite on storage whose connection settings come from
/// <c>UsePostgreSqlEntityFramework&lt;TContext&gt;()</c>: the data source or connection string EF Core holds for the
/// context, rather than settings the application passes to messaging.
/// </summary>
[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlEntityFrameworkStorageTests(PostgreSqlTestFixture fixture)
    : PostgreSqlStorageTests(fixture)
{
    private ServiceProvider? _registration;

    /// <inheritdoc />
    protected override bool RegisteredFromEntityFrameworkContext => true;

    /// <inheritdoc />
    protected override IOptions<PostgreSqlOptions> ResolveStorageOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<StorageConformanceDbContext>(options => options.UseNpgsql(Fixture.ConnectionString));
        services.AddHeadlessMessaging(setup => setup.UsePostgreSqlEntityFramework<StorageConformanceDbContext>());
        _registration = services.BuildServiceProvider();

        return _registration.GetRequiredService<IOptions<PostgreSqlOptions>>();
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
