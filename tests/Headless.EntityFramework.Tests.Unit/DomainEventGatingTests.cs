// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// With the built-in dispatcher, a save synthesizes lifecycle events and opens a transaction only for events some
/// handler observes.
/// </summary>
public sealed class DomainEventGatingTests : TestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_save_without_transaction_or_events_when_no_handler_is_registered(bool synchronous)
    {
        // given
        await using var harness = await Harness.CreateAsync(_ => { });
        var db = harness.CreateContext();
        var order = new Order { Id = Guid.CreateVersion7(), Name = "a" };
        order.Place();
        db.Add(order);

        // when
        _ = synchronous ? db.SaveChanges() : await db.SaveChangesAsync(AbortToken);

        // then
        harness.Transactions.Should().Be(0);
        harness.Detections.Should().Be(2);
        order.GetDomainEvents().Should().BeEmpty();
        (await harness.CreateContext().Set<Order>().CountAsync(AbortToken)).Should().Be(1);
    }

    [Fact]
    public async Task should_dispatch_only_handled_lifecycle_event_inside_a_transaction()
    {
        // given
        var recorder = new Recorder();
        await using var harness = await Harness.CreateAsync(services =>
        {
            services.AddSingleton(recorder);
            services.AddScoped<IDomainEventHandler<EntityCreatedEventData<Order>>, CreatedHandler>();
        });
        var db = harness.CreateContext();

        // when
        db.Add(new Order { Id = Guid.CreateVersion7(), Name = "a" });
        await db.SaveChangesAsync(AbortToken);

        // then
        recorder.Created.Should().Be(1);
        recorder.SawTransaction.Should().BeTrue();
        harness.Transactions.Should().Be(1);
    }

    [Fact]
    public async Task should_dispatch_explicit_event_inside_a_transaction_when_it_has_a_handler()
    {
        // given
        var recorder = new Recorder();
        await using var harness = await Harness.CreateAsync(services =>
        {
            services.AddSingleton(recorder);
            services.AddScoped<IDomainEventHandler<OrderPlaced>, PlacedHandler>();
        });
        var db = harness.CreateContext();
        var order = new Order { Id = Guid.CreateVersion7(), Name = "a" };
        order.Place();

        // when
        db.Add(order);
        await db.SaveChangesAsync(AbortToken);

        // then
        recorder.Placed.Should().Be(1);
        recorder.SawTransaction.Should().BeTrue();
        order.GetDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task should_dispatch_lifecycle_events_to_open_generic_handler()
    {
        // given
        var recorder = new Recorder();
        await using var harness = await Harness.CreateAsync(services =>
        {
            services.AddSingleton(recorder);
            services.AddScoped(typeof(IDomainEventHandler<>), typeof(AnyEventHandler<>));
        });
        var db = harness.CreateContext();

        // when
        db.Add(new Order { Id = Guid.CreateVersion7(), Name = "a" });
        await db.SaveChangesAsync(AbortToken);

        // then: created and changed both reach the open generic handler
        recorder.Any.Should().Be(2);
        harness.Transactions.Should().Be(1);
    }

    [Fact]
    public async Task should_synthesize_and_dispatch_every_lifecycle_event_to_custom_dispatcher()
    {
        // given
        var dispatcher = new CountingDispatcher();
        await using var harness = await Harness.CreateAsync(
            services => services.AddSingleton<IDomainEventDispatcher>(dispatcher),
            addDomainEvents: false
        );
        var db = harness.CreateContext();

        // when
        db.Add(new Order { Id = Guid.CreateVersion7(), Name = "a" });
        await db.SaveChangesAsync(AbortToken);

        // then
        dispatcher
            .Payloads.Should()
            .ContainSingle(p => p is EntityCreatedEventData<Order>)
            .And.ContainSingle(p => p is EntityChangedEventData<Order>);
    }

    public sealed record OrderPlaced;

    public sealed class Order : AggregateRoot<Guid>
    {
        public string Name { get; set; } = "";

        public void Place() => AddDomainEvent(new OrderPlaced());
    }

    private sealed class Recorder
    {
        public int Created { get; set; }

        public int Placed { get; set; }

        public int Any { get; set; }

        public bool SawTransaction { get; set; }
    }

    private sealed class CreatedHandler(Recorder recorder, OrdersDbContext db)
        : IDomainEventHandler<EntityCreatedEventData<Order>>
    {
        public ValueTask HandleAsync(
            EventContext<EntityCreatedEventData<Order>> context,
            CancellationToken cancellationToken = default
        )
        {
            recorder.Created++;
            recorder.SawTransaction = db.Database.CurrentTransaction is not null;

            return ValueTask.CompletedTask;
        }
    }

    private sealed class PlacedHandler(Recorder recorder, OrdersDbContext db) : IDomainEventHandler<OrderPlaced>
    {
        public ValueTask HandleAsync(EventContext<OrderPlaced> context, CancellationToken cancellationToken = default)
        {
            recorder.Placed++;
            recorder.SawTransaction = db.Database.CurrentTransaction is not null;

            return ValueTask.CompletedTask;
        }
    }

    private sealed class AnyEventHandler<T>(Recorder recorder) : IDomainEventHandler<T>
        where T : class
    {
        public ValueTask HandleAsync(EventContext<T> context, CancellationToken cancellationToken = default)
        {
            recorder.Any++;

            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingDispatcher : IDomainEventDispatcher
    {
        public List<object> Payloads { get; } = [];

        public ValueTask DispatchAsync<TPayload>(
            EventContext<TPayload> context,
            CancellationToken cancellationToken = default
        )
            where TPayload : class
        {
            Payloads.Add(context.Payload);

            return ValueTask.CompletedTask;
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly List<AsyncServiceScope> _scopes = [];
        private ServiceProvider _provider = null!;

        public int Transactions { get; private set; }

        public int Detections { get; private set; }

        public static async Task<Harness> CreateAsync(Action<IServiceCollection> configure, bool addDomainEvents = true)
        {
            var harness = new Harness();
            await harness._connection.OpenAsync(AbortToken);
            var services = new ServiceCollection();
            services.AddLogging();
            var builder = services.AddHeadlessDbContext<OrdersDbContext>(options =>
                options
                    .UseSqlite(harness._connection)
                    .LogTo(
                        (id, _) =>
                            id == RelationalEventId.TransactionStarted || id == CoreEventId.DetectChangesStarting,
                        data => harness._Count(data.EventId)
                    )
            );

            if (addDomainEvents)
            {
                builder.AddDomainEvents();
            }

            configure(services);
            harness._provider = services.BuildServiceProvider();
            await harness.CreateContext().Database.EnsureCreatedAsync(AbortToken);
            harness.Transactions = 0;
            harness.Detections = 0;

            return harness;
        }

        public OrdersDbContext CreateContext()
        {
            var scope = _provider.CreateAsyncScope();
            _scopes.Add(scope);

            return scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        }

        private void _Count(EventId eventId)
        {
            if (eventId == CoreEventId.DetectChangesStarting)
            {
                Detections++;
            }
            else
            {
                Transactions++;
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var scope in _scopes)
            {
                await scope.DisposeAsync();
            }

            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    public sealed class OrdersDbContext(DbContextOptions options) : HeadlessDbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        public override string DefaultSchema => "";
    }
}
