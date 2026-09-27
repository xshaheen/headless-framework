// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Security;
using Headless.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Provider-neutral contract for a raw-ADO settings storage fixture. Each leaf fixture owns its own
/// Testcontainers instance (PostgreSQL or SQL Server) and implements these members; the shared host bootstrap lives
/// in <see cref="SettingsStorageFixtureExtensions" />. The fixtures derive from the provider's container fixture,
/// so the shared part is an interface rather than an abstract base class.
/// </summary>
public interface ISettingsStorageFixture
{
    /// <summary>Connection string to the shared container database.</summary>
    string ConnectionString { get; }

    /// <summary>
    /// The exception type the provider's driver raises when the database rejects a write, such as a value longer
    /// than its column (<c>PostgresException</c> or <c>SqlException</c>).
    /// </summary>
    Type ProviderExceptionType { get; }

    /// <summary>Wires the storage provider for this backend (e.g. <c>setup.UsePostgreSql(connectionString)</c>).</summary>
    void UseStorage(HeadlessSettingsSetupBuilder setup, string connectionString);

    /// <summary>Drops <paramref name="schema" /> with every table and type the provider's initializer creates.</summary>
    Task DropSchemaAsync(string schema, CancellationToken cancellationToken);

    /// <summary>Reports whether <paramref name="tableName" /> exists in <paramref name="schema" />.</summary>
    Task<bool> TableExistsAsync(string schema, string tableName, CancellationToken cancellationToken);
}

/// <summary>Shared host bootstrap for <see cref="ISettingsStorageFixture" /> implementations.</summary>
public static class SettingsStorageFixtureExtensions
{
    /// <summary>
    /// Builds an unstarted host that stores settings in <paramref name="schema" /> through the fixture's provider.
    /// Pass <paramref name="connectionString" /> to point the provider somewhere other than the fixture's database.
    /// </summary>
    public static IHost CreateHost(this ISettingsStorageFixture fixture, string schema, string? connectionString = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        // AddHeadlessSettings registers the management core, which requires IStringEncryptionService.
        builder.Configuration.AddInMemoryCollection([
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultPassPhrase", "TestPassPhrase123456"),
            new KeyValuePair<string, string?>("Headless:StringEncryption:InitVectorBytes", "VGVzdElWMDEyMzQ1Njc4OQ=="),
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultSalt", "VGVzdFNhbHQ="),
        ]);
        builder.Services.AddStringEncryptionService(
            builder.Configuration.GetRequiredSection("Headless:StringEncryption")
        );
        // The value store caches every read, and the host refuses to start without a registered cache.
        builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
        builder.Services.AddHeadlessSettings(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = schema);
            fixture.UseStorage(setup, connectionString ?? fixture.ConnectionString);
        });

        return builder.Build();
    }
}
