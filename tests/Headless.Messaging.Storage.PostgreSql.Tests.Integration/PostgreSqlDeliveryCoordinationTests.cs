// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Coordination;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Storage.PostgreSql;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tests;

/// <summary>
/// The storage's "is this unit on my database?" answer, checked at the storage itself against a live Npgsql
/// transaction: a loopback spelling of the same server is accepted, while a database name that differs only by
/// case is refused, because PostgreSQL database names are case-sensitive.
/// </summary>
[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlDeliveryCoordinationTests(PostgreSqlTestFixture fixture) : TestBase
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
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        builder.Host = _SwapLoopback(builder.Host);

        var coordination = await _ResolveAsync(builder.ConnectionString);

        coordination.Status.Should().Be(DeliveryCoordinationStatus.Compatible);
    }

    [Fact]
    public async Task should_refuse_a_unit_when_the_configured_database_differs_only_by_case()
    {
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        builder.Database = builder.Database!.ToUpperInvariant();

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
        services.AddDbContext<CoordinationDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.Configure<PostgreSqlOptions>(x => x.ConnectionString = configuredConnectionString);
        services.Configure<MessagingOptions>(x => x.Version = "v1");
        services.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>();
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();
        services.AddSingleton(TimeProvider.System);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoordinationDbContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
        IDeliveryCoordinationResolver storage = new PostgreSqlDataStorage(
            provider.GetRequiredService<IOptions<PostgreSqlOptions>>(),
            TestStorageOptions.For(),
            provider.GetRequiredService<IOptions<MessagingOptions>>(),
            provider.GetRequiredService<IStorageInitializer>(),
            provider.GetRequiredService<ISerializer>(),
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            TimeProvider.System,
            new NullNodeMembership(),
            NullLogger<PostgreSqlDataStorage>.Instance
        );

        await using var unit = await factory.BeginAsync(db, cancellationToken: AbortToken);

        return storage.Resolve(unit);
    }

    private static string _SwapLoopback(string? host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return "127.0.0.1";
        }

        if (string.Equals(host, "127.0.0.1", StringComparison.Ordinal))
        {
            return "localhost";
        }

        Assert.Skip($"The container host '{host}' is not a loopback address, so there is no alias to swap.");

        return host!;
    }

    /// <summary>A context with no model, used only to open the unit's connection and transaction.</summary>
    private sealed class CoordinationDbContext(DbContextOptions<CoordinationDbContext> options) : DbContext(options);
}
