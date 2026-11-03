// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Messaging.Testing;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

// Top-level and public on purpose: the harness's transport recorder resolves a published payload back to its
// contract type by name, which a private nested type does not expose.
public sealed record SavedOrderShipped(string UniqueId);

/// <summary>
/// A <see cref="HeadlessDbContext"/> save over a real database, with in-memory messaging storage: the integration
/// event an entity emits rides the save's unit of work, whose resource is the database transaction, and becomes a
/// published message only when that save commits.
/// </summary>
public sealed class OutboxIntegrationEventSaveTests : TestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_publish_an_integration_event_once_the_pipeline_owned_save_commits(bool sync)
    {
        // given — no transaction on the context: the pipeline opens one and enlists it in an observed unit
        await using var host = await _CreateHostAsync(
            nameof(should_publish_an_integration_event_once_the_pipeline_owned_save_commits) + sync
        );
        await using var scope = host.Harness.ServiceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        db.Orders.Add(_OrderShipping("pipeline-owned"));

        // when
        if (sync)
        {
            db.SaveChanges();
        }
        else
        {
            await db.SaveChangesAsync(AbortToken);
        }

        // then — the row committed and the message was promoted and dispatched after the commit
        await host.Harness.WaitForPublishedAsync<SavedOrderShipped>(
            message => string.Equals(message.UniqueId, "pipeline-owned", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5),
            AbortToken
        );
        (await _DurableMessageCountAsync(host.Harness)).Should().Be(1);
        (await _OrderCountAsync(host.Harness)).Should().Be(1);
    }

    [Fact]
    public async Task should_publish_nothing_when_the_caller_owned_unit_rolls_back()
    {
        // given — the save runs inside a unit the caller began on the context
        await using var host = await _CreateHostAsync(
            nameof(should_publish_nothing_when_the_caller_owned_unit_rolls_back)
        );
        await using var scope = host.Harness.ServiceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();

        // when
        await using (var unit = await factory.BeginAsync(db, AbortToken))
        {
            db.Orders.Add(_OrderShipping("rolled-back"));
            await db.SaveChangesAsync(AbortToken);

            // The message waits on the unit until it commits.
            (await _DurableMessageCountAsync(host.Harness))
                .Should()
                .Be(0);

            await unit.RollbackAsync();
        }

        // then — neither the order nor its message survived
        (await _DurableMessageCountAsync(host.Harness))
            .Should()
            .Be(0);
        (await _OrderCountAsync(host.Harness)).Should().Be(0);
        host.Harness.Published.Should().BeEmpty();
    }

    #region Setup

    private static async Task<TestHost> _CreateHostAsync(string databaseName)
    {
        // A named shared-cache database lives as long as one connection holds it open.
        var keeper = new SqliteConnection($"Data Source={databaseName};Mode=Memory;Cache=Shared");
        await keeper.OpenAsync(AbortToken);

        var harness = await MessagingTestHarness.CreateAsync(services =>
        {
            services.AddHeadlessMessaging(setup =>
            {
                setup.UseInMemory();
                setup.UseInMemoryStorage();
            });
            services.ConfigureMessaging(messaging => messaging.Message<SavedOrderShipped>("saved-order-shipped"));
            services
                .AddHeadlessDbContext<OrdersDbContext>(options => options.UseSqlite(keeper.ConnectionString))
                .AddDomainEvents()
                .AddIntegrationEventOutbox();
        });

        await using (var scope = harness.ServiceProvider.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<OrdersDbContext>()
                .GetService<IRelationalDatabaseCreator>()
                .CreateTablesAsync(AbortToken);
        }

        return new TestHost(harness, keeper);
    }

    private static Order _OrderShipping(string uniqueId)
    {
        var order = new Order { Name = uniqueId };
        order.Ship(new SavedOrderShipped(uniqueId));

        return order;
    }

    private static async Task<int> _DurableMessageCountAsync(MessagingTestHarness harness)
    {
        var page = await harness
            .ServiceProvider.GetRequiredService<IDataStorage>()
            .GetMonitoringApi()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Publish,
                    CurrentPage = 0,
                    PageSize = 20,
                },
                AbortToken
            );

        return page.Items.Count;
    }

    private static async Task<int> _OrderCountAsync(MessagingTestHarness harness)
    {
        await using var scope = harness.ServiceProvider.CreateAsyncScope();

        return await scope
            .ServiceProvider.GetRequiredService<OrdersDbContext>()
            .Orders.AsNoTracking()
            .CountAsync(AbortToken);
    }

    private sealed class TestHost(MessagingTestHarness harness, SqliteConnection keeper) : IAsyncDisposable
    {
        public MessagingTestHarness Harness { get; } = harness;

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            await keeper.DisposeAsync();
        }
    }

    #endregion

    #region Test model

    public sealed class Order : AggregateRoot, IEntity<Guid>
    {
        public Guid Id { get; private init; }

        public required string Name { get; init; }

        public void Ship(object integrationEvent) => AddIntegrationEvent(integrationEvent);

        public override IReadOnlyList<object> GetKeys() => [Id];
    }

    public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : HeadlessDbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        public override string DefaultSchema => "";
    }

    #endregion
}
