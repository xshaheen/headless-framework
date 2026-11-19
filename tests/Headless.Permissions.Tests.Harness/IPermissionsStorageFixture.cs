// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Caching;
using Headless.Hosting;
using Headless.Permissions;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Provider-neutral contract for a raw-ADO permissions storage fixture. Each leaf fixture owns its own
/// Testcontainers instance (PostgreSQL or SQL Server) and implements these members; the shared host bootstrap lives
/// in <see cref="PermissionsStorageFixtureExtensions" />. The fixtures derive from the provider's container fixture,
/// so the shared part is an interface rather than an abstract base class.
/// </summary>
public interface IPermissionsStorageFixture
{
    /// <summary>Connection string to the shared container database.</summary>
    string ConnectionString { get; }

    /// <summary>A connection string to another database on the same server, for refusal tests.</summary>
    string OtherDatabaseConnectionString { get; }

    /// <summary>The naming style of the fixture's database, which decides the default object names.</summary>
    StorageNamingStyle NamingStyle { get; }

    /// <summary>Wires the storage provider for this backend (e.g. <c>setup.UsePostgreSql(connectionString)</c>).</summary>
    void UseStorage(HeadlessPermissionsSetupBuilder setup, string connectionString);

    /// <summary>Points <paramref name="builder" /> at this backend's EF Core provider (no connection is opened).</summary>
    void UseEntityFrameworkProvider(DbContextOptionsBuilder builder, string connectionString);

    /// <summary>
    /// Drops <paramref name="schema" /> with every object in it, the schema runner's history included: the runner
    /// trusts that history, so one that outlived the tables would stop them being recreated.
    /// </summary>
    Task DropSchemaAsync(string schema, CancellationToken cancellationToken);

    /// <summary>Reports whether <paramref name="tableName" /> exists in <paramref name="schema" />.</summary>
    Task<bool> TableExistsAsync(string schema, string tableName, CancellationToken cancellationToken);

    /// <summary>Counts the rows of <paramref name="tableName" /> in <paramref name="schema" />.</summary>
    Task<long> CountRowsAsync(string schema, string tableName, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the objects the initializer created in <paramref name="schema" />: every column as <c>table.column</c>
    /// and every index, primary key included, as <c>table.index</c>.
    /// </summary>
    Task<PermissionsStoreObjects> ReadStoreObjectsAsync(string schema, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a statement-level insert trigger on the grants table of <paramref name="schema" /> that counts the
    /// <c>INSERT</c> commands the store sends, read back with <see cref="ReadInsertCommandCountAsync" />.
    /// </summary>
    Task CreateInsertCommandCounterAsync(string schema, CancellationToken cancellationToken);

    /// <summary>Reads the count the trigger from <see cref="CreateInsertCommandCounterAsync" /> kept.</summary>
    Task<int> ReadInsertCommandCountAsync(string schema, CancellationToken cancellationToken);

    /// <summary>
    /// The exception type the provider's driver raises when the database rejects a write, such as a value longer
    /// than its column (<c>PostgresException</c> or <c>SqlException</c>).
    /// </summary>
    Type ProviderExceptionType { get; }

    /// <summary>Creates an unopened provider connection.</summary>
    DbConnection CreateConnection(string connectionString);

    /// <summary>Begins an owned raw-ADO unit of work on <paramref name="connection"/>.</summary>
    ValueTask<IUnitOfWork> BeginUnitOfWorkAsync(
        IUnitOfWorkFactory factory,
        DbConnection connection,
        CancellationToken cancellationToken
    );

    /// <summary>Enlists a caller-committed <paramref name="transaction"/> as an observed unit of work.</summary>
    IUnitOfWork EnlistUnitOfWork(IUnitOfWorkFactory factory, DbConnection connection, DbTransaction transaction);
}

/// <summary>A schema's columns and indexes as <c>table.name</c> strings, compared between raw DDL and the EF model.</summary>
public sealed record PermissionsStoreObjects(IReadOnlySet<string> Columns, IReadOnlySet<string> Indexes);

/// <summary>Shared host bootstrap for <see cref="IPermissionsStorageFixture" /> implementations.</summary>
public static class PermissionsStorageFixtureExtensions
{
    /// <summary>
    /// Builds an unstarted host that stores permissions in <paramref name="schema" /> through the fixture's provider.
    /// <paramref name="configureStorage" /> adjusts the storage options after the schema is set, and
    /// <paramref name="configureServices" /> registers services before the permissions registration.
    /// </summary>
    public static IHost CreateHost(
        this IPermissionsStorageFixture fixture,
        string schema,
        Action<PermissionsStorageOptions>? configureStorage = null,
        Action<IServiceCollection>? configureServices = null
    )
    {
        var builder = Host.CreateApplicationBuilder();
        configureServices?.Invoke(builder.Services);
        builder.Services.AddSingleton(TimeProvider.System);
        // Grant caching is required, and the host refuses to start without a registered cache.
        builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
        builder.Services.AddHeadlessPermissions(setup =>
        {
            setup.ConfigureStorage(options =>
            {
                options.Schema = schema;
                configureStorage?.Invoke(options);
            });
            fixture.UseStorage(setup, fixture.ConnectionString);
        });
        builder.Services.AddDeployStepsOnStartup(o => o.Seed = false);

        return builder.Build();
    }

    /// <summary>The permission grants table name the fixture's database uses by default.</summary>
    public static string GrantsTable(this IPermissionsStorageFixture fixture)
    {
        return new PermissionsStorageOptions().ResolvePermissionGrantsTableName(fixture.NamingStyle);
    }
}
