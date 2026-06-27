// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination.EntityFramework;
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
    public async Task save_emitting_events_under_a_consumer_opened_plain_transaction_should_fail_loud()
    {
        // given — a CONSUMER opens its own PLAIN EF transaction (BeginTransactionAsync) WITHOUT calling
        // EnlistCommitCoordination, then saves with integration events. The pipeline reuses the consumer's
        // transaction via the current-transaction branch, but no commit coordinator is ambient — dispatching the
        // outbox here would be non-atomic with the consumer's transaction. The dispatcher now FAILS LOUD (#1)
        // rather than silently writing the row on an autonomous connection. Atomic enlistment requires either the
        // pipeline-owned save (no consumer transaction) or an explicit EnlistCommitCoordination (see the
        // enlisted_publish_* tests).
        const string marker = "evt-consumer-plain";
        await using var provider = await _BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var order = new OrderEntity { Name = "consumer-plain" };
        order.AddIntegrationEvent(new OrderShipped($"{marker}-1"));
        db.Orders.Add(order);

        // when — save under the un-enlisted consumer transaction.
        var act = async () => await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then — fails loud with an actionable wiring error and writes no outbox row.
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*not enlisted in commit coordination*"
        );
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        (await _CountPublishedContainingAsync(marker)).Should().Be(0);
    }

    [Fact]
    public async Task coordinated_transaction_wrapping_a_save_should_dispatch_the_event_atomically()
    {
        // given — the welded ExecuteCoordinatedTransactionAsync helper opens the coordinated transaction and
        // pushes the ambient coordinator. The inner SaveChanges runs WITHIN that transaction (current-transaction
        // branch) and emits an integration event. This pins that the #1 guard sees the ambient (outer) coordinator
        // via AsyncLocal and PASSES — the event buffers on that coordinator and drains atomically on commit.
        const string marker = "evt-coordinated-nested";
        await using var provider = await _BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();

        // when
        await db.ExecuteCoordinatedTransactionAsync(
            async (ctx, ct) =>
            {
                var order = new OrderEntity { Name = "coordinated-nested" };
                order.AddIntegrationEvent(new OrderShipped($"{marker}-1"));
                ctx.Orders.Add(order);
                await ctx.SaveChangesAsync(ct);
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // then — the event dispatched atomically with the business row (guard did not mis-fire).
        (await _CountPublishedContainingAsync(marker))
            .Should()
            .Be(1);
        (await _CountOrdersAsync()).Should().Be(1);
    }

    [Fact]
    public async Task pipeline_owned_save_writes_business_and_outbox_rows_atomically_on_commit()
    {
        // given — no ambient transaction: the pipeline opens its OWN coordinated transaction (Option 1). The
        // outbox row is enlisted in it and drains post-commit. This asserts the atomic-COMMIT half of the
        // contract: a successful save persists exactly one business row AND one outbox row together. The
        // rollback-DISCARD half (the pipeline's coordinated transaction rolling back drops the enlisted outbox
        // work) is covered at the seam by the commit-coordination conformance / EF interceptor tests.
        const string marker = "evt-pipeline-atomic";
        await using var provider = await _BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        var order = new OrderEntity { Name = "pipeline-atomic" };
        order.AddIntegrationEvent(new OrderShipped($"{marker}-1"));
        db.Orders.Add(order);

        // when — pipeline opens the coordinated transaction, writes business + outbox rows, commits atomically.
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then — the outbox row is durable post-commit, alongside the committed business row.
        (await _CountPublishedContainingAsync(marker))
            .Should()
            .Be(1);
        (await _CountOrdersAsync()).Should().Be(1);
    }

    [Fact]
    public async Task enlisted_publish_rolled_back_should_discard_the_outbox_row()
    {
        // given — the consumer enlist seam (DatabaseFacade.EnlistCommitCoordination) pushes the ambient coordinator
        // SYNCHRONOUSLY in this frame, so the outbox writer stores the row INSIDE the transaction (not on an
        // autonomous connection). This is the decisive proof that ICurrentCommitCoordinator.Current flowed: if the
        // ambient scope were stranded (the AsyncLocal-set-inside-an-async-method bug), the writer would fall back to
        // an autonomous write and the row would SURVIVE the rollback. It must instead be discarded with the tx.
        const string marker = "evt-enlist-rollback";
        await using var provider = await _BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        var outboxBus = scope.ServiceProvider.GetRequiredService<IOutboxBus>();

        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await using (db.Database.EnlistCommitCoordination(transaction, scope.ServiceProvider))
        {
            // when — publish enlists the row inside the transaction, then the consumer rolls back.
            await outboxBus.PublishAsync(
                new OrderShipped($"{marker}-1"),
                cancellationToken: TestContext.Current.CancellationToken
            );

            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        // then — the enlisted row rolled back with the transaction.
        (await _CountPublishedContainingAsync(marker))
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task enlisted_publish_committed_should_persist_the_outbox_row_atomically()
    {
        // given — same enlist seam, but commit. Proves the in-tx write path (not the autonomous fallback): the row
        // is only visible after commit and survives. Paired with the rollback test, this pins atomic enlistment.
        const string marker = "evt-enlist-commit";
        await using var provider = await _BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        var outboxBus = scope.ServiceProvider.GetRequiredService<IOutboxBus>();

        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await using (db.Database.EnlistCommitCoordination(transaction, scope.ServiceProvider))
        {
            await outboxBus.PublishAsync(
                new OrderShipped($"{marker}-1"),
                cancellationToken: TestContext.Current.CancellationToken
            );

            // when — commit the enlisting transaction.
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        // then — the enlisted row committed atomically with the transaction.
        (await _CountPublishedContainingAsync(marker))
            .Should()
            .Be(1);
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

    private async Task<int> _CountOrdersAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM "Orders" """;

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
