// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Coordination;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Storage.SqlServer;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// The storage's "is this unit on my database?" answer, checked at the storage itself against a live SqlClient
/// transaction: a loopback spelling of the same server is accepted, while a configured catalog whose casing differs
/// from the database's name is refused, because an open connection reports the name as the server stores it.
/// </summary>
[Collection<SqlServerTestFixture>]
public sealed class SqlServerDeliveryCoordinationTests(SqlServerTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_accept_a_unit_on_the_configured_database()
    {
        var coordination = await _ResolveAsync(fixture.ConnectionString);

        coordination.Status.Should().Be(DeliveryCoordinationStatus.Compatible);
    }

    [Fact]
    public async Task should_accept_a_unit_when_the_configured_host_is_another_loopback_spelling()
    {
        var builder = new SqlConnectionStringBuilder(fixture.ConnectionString);
        builder.DataSource = _SwapLoopback(builder.DataSource);

        var coordination = await _ResolveAsync(builder.ConnectionString);

        coordination.Status.Should().Be(DeliveryCoordinationStatus.Compatible);
    }

    [Fact]
    public async Task should_refuse_a_unit_when_the_configured_catalog_differs_only_by_case()
    {
        var builder = new SqlConnectionStringBuilder(fixture.ConnectionString);
        builder.InitialCatalog = builder.InitialCatalog.ToUpperInvariant();

        var coordination = await _ResolveAsync(builder.ConnectionString);

        coordination.Status.Should().Be(DeliveryCoordinationStatus.Incompatible);
        coordination.Mismatch.Should().Be(DeliveryCoordinationMismatch.Database);
    }

    private async Task<DeliveryCoordination> _ResolveAsync(string configuredConnectionString)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.AddEntityFrameworkUnitOfWork();
        services.AddDbContext<CoordinationDbContext>(options => options.UseSqlServer(fixture.ConnectionString));
        services.Configure<SqlServerOptions>(x => x.ConnectionString = configuredConnectionString);
        services.Configure<MessagingOptions>(x => x.Version = "v1");
        services.AddSingleton<IStorageInitializer, SqlServerStorageInitializer>();
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoordinationDbContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
        IDeliveryCoordinationResolver storage = new SqlServerDataStorage(
            provider.GetRequiredService<IOptions<MessagingOptions>>(),
            provider.GetRequiredService<IOptions<SqlServerOptions>>(),
            TestStorageOptions.For(),
            provider.GetRequiredService<IStorageInitializer>(),
            provider.GetRequiredService<ISerializer>(),
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            new NullNodeMembership(),
            NullLogger<SqlServerDataStorage>.Instance
        );

        await using var unit = await factory.BeginAsync(db, cancellationToken: AbortToken);

        return storage.Resolve(unit);
    }

    private static string _SwapLoopback(string dataSource)
    {
        if (dataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat("127.0.0.1", dataSource.AsSpan("localhost".Length));
        }

        if (dataSource.StartsWith("127.0.0.1", StringComparison.Ordinal))
        {
            return string.Concat("localhost", dataSource.AsSpan("127.0.0.1".Length));
        }

        Assert.Skip($"The container host '{dataSource}' is not a loopback address, so there is no alias to swap.");

        return dataSource;
    }

    /// <summary>A context with no model, used only to open the unit's connection and transaction.</summary>
    private sealed class CoordinationDbContext(DbContextOptions<CoordinationDbContext> options) : DbContext(options);
}
