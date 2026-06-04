// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Tests;

/// <summary>
/// Proves the outbox bridge writes integration-event rows into the messaging outbox enlisted in the EF save
/// transaction: rows are atomic with the business data, rolled back together, isolated until commit, identical
/// across the sync/async save paths, and each concrete event type is routed through its own publish overload.
/// </summary>
[Collection<OutboxBridgeTestFixture>]
public sealed class OutboxBridgeIntegrationTests(OutboxBridgeTestFixture fixture) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                TRUNCATE TABLE messaging."published" CASCADE;
                TRUNCATE TABLE messaging."received" CASCADE;
                TRUNCATE TABLE "Orders" CASCADE;
                """;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        catch (PostgresException)
        {
            // Schema/table might not exist yet
        }
    }

    [Fact]
    public async Task save_emitting_an_integration_event_should_write_one_outbox_row()
    {
        // given
        const string marker = "evt-single";
        await using var provider = await _BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        var order = new OrderEntity { Name = "ship" };
        order.AddIntegrationEvent(new OrderShipped($"{marker}-1"));
        db.Orders.Add(order);

        // when — no ambient transaction: the pipeline opens one, writes business + outbox rows, commits.
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then
        (await _CountPublishedContainingAsync(marker))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task save_emitting_multiple_concrete_event_types_should_route_each_to_its_own_overload()
    {
        // given
        const string marker = "evt-multi";
        await using var provider = await _BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        var order = new OrderEntity { Name = "multi" };
        order.AddIntegrationEvent(new OrderShipped($"{marker}-shipped"));
        order.AddIntegrationEvent(new OrderInvoiced($"{marker}-invoiced"));
        db.Orders.Add(order);

        // when
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then — both concrete types persisted (compiled invoker resolved each closed generic correctly)
        (await _CountPublishedContainingAsync($"{marker}-shipped"))
            .Should()
            .Be(1);
        (await _CountPublishedContainingAsync($"{marker}-invoiced")).Should().Be(1);
    }

    [Fact]
    public async Task sync_save_should_write_outbox_rows_identically_to_async()
    {
        // given
        const string marker = "evt-sync";
        await using var provider = await _BuildProviderAsync();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        var order = new OrderEntity { Name = "sync" };
        order.AddIntegrationEvent(new OrderShipped($"{marker}-1"));
        db.Orders.Add(order);

        // when — sync save path drives the sync Dispatch (sync-over-async) bridge.
        // ReSharper disable once MethodHasAsyncOverload
        db.SaveChanges();

        // then
        (await _CountPublishedContainingAsync(marker))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task rolled_back_save_should_leave_no_outbox_rows()
    {
        // given
        const string marker = "evt-rollback";
        await using var provider = await _BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var order = new OrderEntity { Name = "rollback" };
        order.AddIntegrationEvent(new OrderShipped($"{marker}-1"));
        db.Orders.Add(order);

        // when — save writes outbox rows inside the ambient transaction (pipeline does not commit it),
        // then the caller rolls back. Outbox rows must die with the business data.
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);

        // then
        (await _CountPublishedContainingAsync(marker))
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task outbox_rows_should_be_invisible_to_other_connections_until_commit()
    {
        // given
        const string marker = "evt-isolation";
        await using var provider = await _BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var order = new OrderEntity { Name = "iso" };
        order.AddIntegrationEvent(new OrderShipped($"{marker}-1"));
        db.Orders.Add(order);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // when — a separate connection cannot see the uncommitted outbox row (proves it was written on the
        // EF save transaction, not a second autonomous connection).
        var beforeCommit = await _CountPublishedContainingAsync(marker);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        var afterCommit = await _CountPublishedContainingAsync(marker);

        // then
        beforeCommit.Should().Be(0);
        afterCommit.Should().Be(1);
    }

    #region Setup

    private async Task<ServiceProvider> _BuildProviderAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessDbContextServices().AddDomainEvents().AddIntegrationEventOutbox();

        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UsePostgreSql(fixture.ConnectionString);
        });

        services.AddDbContext<BridgeTestDbContext>(options =>
            options.UseNpgsql(fixture.ConnectionString).AddHeadlessExtension()
        );

        var provider = services.BuildServiceProvider();

        // Initialize messaging outbox tables and EF business tables in the shared database. The messaging host
        // is intentionally not started, so the relay never drains rows — outbox-row assertions stay deterministic.
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(TestContext.Current.CancellationToken);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();

        // EnsureCreated would no-op (Testcontainers already created the database, so it considers the schema
        // present). Create the model's tables directly; ignore duplicate-table when a prior test already did.
        var creator = db.GetService<IRelationalDatabaseCreator>();
        try
        {
            await creator.CreateTablesAsync(TestContext.Current.CancellationToken);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.DuplicateTable)
        {
            // Orders table already created by an earlier test in this collection.
        }

        return provider;
    }

    private async Task<int> _CountPublishedContainingAsync(string marker)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM messaging."published" WHERE "Content" LIKE @marker""";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "marker";
        parameter.Value = $"%{marker}%";
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    #endregion

    #region Test model

    private sealed record OrderShipped(string UniqueId) : IIntegrationEvent;

    private sealed record OrderInvoiced(string UniqueId) : IIntegrationEvent;

    private sealed class OrderEntity : AggregateRoot, IEntity<Guid>
    {
        public Guid Id { get; private init; }

        public required string Name { get; init; }

        public override IReadOnlyList<object> GetKeys() => [Id];
    }

    private sealed class BridgeTestDbContext(HeadlessDbContextServices services, DbContextOptions options)
        : HeadlessDbContext(services, options)
    {
        public DbSet<OrderEntity> Orders => Set<OrderEntity>();

        public override string DefaultSchema => "";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<OrderEntity>().Property(e => e.Id).ValueGeneratedNever();
        }
    }

    #endregion
}
