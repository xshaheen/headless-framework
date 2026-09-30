// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.Coordination;
using Headless.DistributedLocks;
using Headless.Hosting.Initialization;
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Security;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// Proves that every relational feature, configured with nothing but one shared connection, lands in the one
/// <see cref="HeadlessStorageDefaults.Schema" /> schema without name collisions, survives initializers racing the
/// shared <c>CREATE SCHEMA</c> from concurrently starting hosts, and treats a later startup as a no-op.
/// </summary>
/// <remarks>
/// Jobs is left out of the concurrent hosts on purpose: its tables come from EF Core's create script, which is not
/// idempotent and which the consuming application owns, so it cannot take part in an initializer race.
/// </remarks>
public abstract class SharedSchemaTestsBase : TestBase
{
    /// <summary>The tables the Jobs EF Core model creates, in this provider's naming convention.</summary>
    protected abstract string[] ExpectedJobsTables { get; }

    /// <summary>An object the catalog reports, with its schema and the provider's type code for it.</summary>
    protected sealed record CatalogObject(string Schema, string Name, string Type);

    /// <summary>Creates an empty, uniquely named database and returns its connection string.</summary>
    protected abstract Task<string> CreateDatabaseAsync(CancellationToken cancellationToken);

    /// <summary>Drops the database created by <see cref="CreateDatabaseAsync" />.</summary>
    protected abstract Task DropDatabaseAsync(string connectionString, CancellationToken cancellationToken);

    /// <summary>Registers the one connection every feature reads through its parameterless provider overload.</summary>
    protected abstract void AddSharedConnection(IServiceCollection services, string connectionString);

    /// <summary>
    /// Registers every raw-provider family through its parameterless provider overload, with no schema
    /// configuration.
    /// </summary>
    protected abstract void AddRawFamilies(IServiceCollection services);

    /// <summary>Selects this provider's messaging storage through the parameterless overload.</summary>
    protected abstract void UseMessagingStorage(MessagingSetupBuilder setup);

    /// <summary>Points the Jobs EF Core store at the database.</summary>
    protected abstract void UseJobsStore(DbContextOptionsBuilder db, string connectionString);

    /// <summary>Every framework-created object the catalog reports, in any non-system schema.</summary>
    protected abstract Task<IReadOnlyList<CatalogObject>> ReadCatalogAsync(
        string connectionString,
        CancellationToken cancellationToken
    );

    /// <summary>The user-created schemas in the database, excluding the provider's built-in ones.</summary>
    protected abstract Task<IReadOnlyList<string>> ReadUserSchemasAsync(
        string connectionString,
        CancellationToken cancellationToken
    );

    /// <summary>The tables each raw family is expected to create, keyed by family, so a rename or collision fails.</summary>
    protected abstract IReadOnlyDictionary<string, string[]> ExpectedRawTables { get; }

    /// <summary>The sequences each raw family is expected to create.</summary>
    protected abstract IReadOnlyDictionary<string, string[]> ExpectedRawSequences { get; }

    /// <summary>The catalog type code this provider reports for a table.</summary>
    protected abstract string TableType { get; }

    /// <summary>The catalog type code this provider reports for a sequence.</summary>
    protected abstract string SequenceType { get; }

    /// <summary>Asserts provider-specific identifier constraints, such as PostgreSQL's silent truncation.</summary>
    protected virtual void AssertIdentifierLimits(IReadOnlyList<CatalogObject> objects) { }

    [Fact]
    public async Task should_initialize_every_feature_into_the_shared_schema_when_hosts_start_concurrently()
    {
        // given
        var connectionString = await CreateDatabaseAsync(AbortToken);

        try
        {
            await _RunScenarioAsync(connectionString);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, CancellationToken.None);
        }
    }

    private async Task _RunScenarioAsync(string connectionString)
    {
        using var hostA = _BuildHost(connectionString, "node-a", includeJobs: false);
        using var hostB = _BuildHost(connectionString, "node-b", includeJobs: false);
        await using var messagingA = _BuildMessaging(connectionString);
        await using var messagingB = _BuildMessaging(connectionString);

        // when: both hosts and both messaging initializers race the empty database at once
        await Task.WhenAll(
            hostA.StartAsync(AbortToken),
            hostB.StartAsync(AbortToken),
            messagingA.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken),
            messagingB.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken)
        );

        // A fenced acquire from each host proves startup created the lock fence sequence it reads.
        await Task.WhenAll(_AcquireFencedLockAsync(hostA), _AcquireFencedLockAsync(hostB));

        // then: every raw family's objects are in the shared schema, and Jobs has none yet
        var rawObjects = await ReadCatalogAsync(connectionString, AbortToken);
        _AssertExpectedObjects(rawObjects, includeJobs: false);

        // when: the consumer creates the Jobs tables in the same schema, then a third host carrying every family
        // starts against the populated database
        using var hostC = _BuildHost(connectionString, "node-c", includeJobs: true);
        await _CreateJobsTablesAsync(hostC);

        var withJobs = await ReadCatalogAsync(connectionString, AbortToken);
        _AssertExpectedObjects(withJobs, includeJobs: true);
        withJobs.Should().Contain(rawObjects, "creating the Jobs tables must leave every other family's objects alone");

        withJobs
            .Except(rawObjects)
            .Where(o => string.Equals(o.Type, TableType, StringComparison.Ordinal))
            .Select(o => o.Name)
            .Should()
            .BeEquivalentTo(ExpectedJobsTables, "the consumer's create script adds only the Jobs model");

        await using var messagingC = _BuildMessaging(connectionString);
        await hostC.StartAsync(AbortToken);
        await messagingC.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        await _AcquireFencedLockAsync(hostC);

        // then: the restart created nothing
        var afterRestart = await ReadCatalogAsync(connectionString, AbortToken);
        afterRestart.Should().BeEquivalentTo(withJobs, "a startup against an initialized schema must be a no-op");

        var userSchemas = await ReadUserSchemasAsync(connectionString, AbortToken);
        userSchemas.Should().Equal([HeadlessStorageDefaults.Schema], "no feature may create objects elsewhere");

        await hostC.StopAsync(AbortToken);
        await hostB.StopAsync(AbortToken);
        await hostA.StopAsync(AbortToken);
    }

    private void _AssertExpectedObjects(IReadOnlyList<CatalogObject> objects, bool includeJobs)
    {
        objects
            .Should()
            .OnlyContain(o => o.Schema == HeadlessStorageDefaults.Schema, "every feature defaults to one schema");

        var tables = objects.Where(o => o.Type == TableType).Select(o => o.Name).ToList();
        var sequences = objects.Where(o => o.Type == SequenceType).Select(o => o.Name).ToList();

        var expectedTables = ExpectedRawTables.SelectMany(f => f.Value).ToList();

        if (includeJobs)
        {
            expectedTables.AddRange(ExpectedJobsTables);
        }

        var expectedSequences = ExpectedRawSequences.SelectMany(f => f.Value).ToList();

        // Every family creates with IF NOT EXISTS, so two families claiming one name would silently share an object;
        // the expected lists staying unique while matching the catalog exactly is what rules that out.
        expectedTables.Should().OnlyHaveUniqueItems("no two families may claim the same table");
        expectedSequences.Should().OnlyHaveUniqueItems("no two families may claim the same sequence");
        tables.Should().BeEquivalentTo(expectedTables);
        sequences.Should().BeEquivalentTo(expectedSequences);
        AssertIdentifierLimits(objects);
    }

    private static async Task _AcquireFencedLockAsync(IHost host)
    {
        var locks = host.Services.GetRequiredService<IDistributedLock>();
        var handle = await locks.AcquireAsync(
            $"shared-schema:{Guid.NewGuid():N}",
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
            AbortToken
        );

        handle.FencingToken.Should().NotBeNull("the fence sequence must exist once a fenced lock is acquired");
        await handle.ReleaseAsync();
    }

    private static async Task _CreateJobsTablesAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
        var creator = (RelationalDatabaseCreator)db.GetService<IDatabaseCreator>();
        await creator.CreateTablesAsync(AbortToken);
    }

    private ServiceProvider _BuildMessaging(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        AddSharedConnection(services, connectionString);
        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.Version = "v1";
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
            UseMessagingStorage(setup);
        });

        return services.BuildServiceProvider();
    }

    private IHost _BuildHost(string connectionString, string nodeId, bool includeJobs)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // Hosted services start concurrently so the families' initializers race each other inside one host as well
        // as across the two hosts.
        builder.Services.Configure<HostOptions>(options => options.ServicesStartConcurrently = true);
        builder.Services.AddSingleton(TimeProvider.System);

        // Settings encrypts values, and the Features and Settings value stores cache reads.
        builder.Configuration.AddInMemoryCollection([
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultPassPhrase", "TestPassPhrase123456"),
            new KeyValuePair<string, string?>("Headless:StringEncryption:InitVectorBytes", "VGVzdElWMDEyMzQ1Njc4OQ=="),
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultSalt", "VGVzdFNhbHQ="),
        ]);
        builder.Services.AddStringEncryptionService(
            builder.Configuration.GetRequiredSection("Headless:StringEncryption")
        );
        builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());

        // Several nodes share this process, so each names its own coordination member.
        builder.Services.AddHeadlessHostIdentity(options => options.HostName = nodeId);

        AddSharedConnection(builder.Services, connectionString);
        AddRawFamilies(builder.Services);

        if (includeJobs)
        {
            builder.Services.AddHeadlessJobs(options =>
            {
                options.DisableBackgroundServices();
                options.UseEntityFramework(ef =>
                    ef.UseJobsDbContext<JobsDbContext>(db => UseJobsStore(db, connectionString))
                );
            });
        }

        return builder.Build();
    }
}
