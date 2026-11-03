// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// An audited save captures its entries once, from the graph the rows describe, and runs a second base save only for a
/// store whose rows sit in the saving context.
/// </summary>
public sealed class AuditPersistencePassTests : TestBase
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task should_run_the_second_base_save_only_when_the_store_adds_rows_to_the_saving_context(
        bool addsToSavingContext,
        int expectedSaves
    )
    {
        // given
        var store = new RecordingStore(addsToSavingContext);
        await using var harness = await Harness.CreateAsync(services => services.AddSingleton<IAuditLogStore>(store));
        var db = harness.CreateContext();
        var order = new Order { Id = Guid.CreateVersion7(), Name = "a" };
        db.Add(order);

        // when
        var saved = await db.SaveChangesAsync(AbortToken);

        // then
        saved.Should().Be(1);
        harness.Saves.Should().Be(expectedSaves);
        store.Saved.Should().ContainSingle().Which.NewValues!["Name"].Should().Be("a");
        db.Entry(order).State.Should().Be(EntityState.Unchanged);
        (await harness.CreateContext().Orders.SingleAsync(AbortToken)).Name.Should().Be("a");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_leave_the_tracker_accepted_when_a_store_writes_its_own_rows(bool modify)
    {
        // given
        var store = new RecordingStore(addsToSavingContext: false);
        await using var harness = await Harness.CreateAsync(services => services.AddSingleton<IAuditLogStore>(store));
        var db = harness.CreateContext();
        var order = new Order { Id = Guid.CreateVersion7(), Name = "a" };
        db.Add(order);
        await db.SaveChangesAsync(AbortToken);
        harness.Saves = 0;
        store.Saved.Clear();

        // when
        if (modify)
        {
            order.Name = "b";
        }
        else
        {
            db.Remove(order);
        }

        // The synchronous twin takes the same shortcut as the async path.
        db.SaveChanges();

        // then
        harness.Saves.Should().Be(1);
        store.Saved.Should().ContainSingle();
        db.Entry(order).State.Should().Be(modify ? EntityState.Unchanged : EntityState.Detached);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_capture_once_after_the_handler_when_a_handled_domain_event_runs(bool synchronous)
    {
        // given
        var store = new RecordingStore(addsToSavingContext: false);
        await using var harness = await Harness.CreateAsync(services =>
        {
            services.AddSingleton<IAuditLogStore>(store);
            services.AddScoped<IDomainEventHandler<EntityCreatedEventData<Order>>, RenamingHandler>();
        });
        var db = harness.CreateContext();
        db.Add(new Order { Id = Guid.CreateVersion7(), Name = "a" });

        // when
        _ = synchronous ? db.SaveChanges() : await db.SaveChangesAsync(AbortToken);

        // then: the one capture ran after the handler, so the stored row carries the handler's change
        harness.Capture.Captures.Should().Be(1);
        store.Saved.Should().ContainSingle().Which.NewValues!["Name"].Should().Be("renamed");
    }

    [Fact]
    public async Task should_capture_once_inside_a_caller_transaction_when_a_consumer_processor_is_registered()
    {
        // given
        var store = new RecordingStore(addsToSavingContext: false);
        await using var harness = await Harness.CreateAsync(
            services =>
            {
                services.AddSingleton<IAuditLogStore>(store);
            },
            options => options.AddSaveEntryProcessor<UpperCaseProcessor>(ServiceLifetime.Singleton)
        );
        var db = harness.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);
        db.Add(new Order { Id = Guid.CreateVersion7(), Name = "a" });

        // when
        await db.SaveChangesAsync(AbortToken);
        await transaction.CommitAsync(AbortToken);

        // then
        harness.Capture.Captures.Should().Be(1);
        store.Saved.Should().ContainSingle().Which.NewValues!["Name"].Should().Be("A");
    }

    [Fact]
    public async Task should_capture_once_and_keep_it_when_no_handler_or_consumer_processor_can_change_the_graph()
    {
        // given
        var store = new RecordingStore(addsToSavingContext: false);
        await using var harness = await Harness.CreateAsync(services =>
        {
            services.AddSingleton<IAuditLogStore>(store);
        });
        var db = harness.CreateContext();
        db.Add(new Order { Id = Guid.CreateVersion7(), Name = "a" });

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        harness.Capture.Captures.Should().Be(1);
        store.Saved.Should().ContainSingle().Which.NewValues!["Name"].Should().Be("a");
    }

    public sealed class Order : AggregateRoot<Guid>
    {
        public string Name { get; set; } = "";
    }

    private sealed class RenamingHandler(OrdersDbContext db) : IDomainEventHandler<EntityCreatedEventData<Order>>
    {
        public ValueTask HandleAsync(
            EventContext<EntityCreatedEventData<Order>> context,
            CancellationToken cancellationToken = default
        )
        {
            foreach (var order in db.ChangeTracker.Entries<Order>())
            {
                order.Entity.Name = "renamed";
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class UpperCaseProcessor : IHeadlessSaveEntryProcessor
    {
        public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
        {
            if (entry is { Entity: Order order, State: EntityState.Added })
            {
                order.Name = order.Name.ToUpperInvariant();
            }
        }
    }

    /// <summary>One entry per changed order, carrying the name it has when captured.</summary>
    private sealed class CountingCapture : IAuditChangeCapture
    {
        public int Captures { get; private set; }

        public IReadOnlyList<AuditLogEntryData> CaptureChanges(IEnumerable<object> entries, AuditCaptureContext context)
        {
            Captures++;

            return
            [
                .. entries
                    .Cast<EntityEntry>()
                    .Where(entry => entry.Entity is Order && entry.State is not EntityState.Unchanged)
                    .Select(entry => new AuditLogEntryData
                    {
                        Action = "entity.changed",
                        EntityType = nameof(Order),
                        NewValues = new() { ["Name"] = ((Order)entry.Entity).Name },
                        CreatedAt = context.Timestamp,
                    }),
            ];
        }
    }

    /// <summary>
    /// Records what it is asked to save. As a raw-SQL store it writes nothing to the context; as a context store it
    /// returns handles without adding rows, which is enough to drive the pipeline's tracker pass.
    /// </summary>
    private sealed class RecordingStore(bool addsToSavingContext) : IAuditLogStore
    {
        public List<AuditLogEntryData> Saved { get; } = [];

        public bool AddsToSavingContext => addsToSavingContext;

        public IReadOnlyList<IAuditLogStoreEntry> Save(IReadOnlyList<AuditLogEntryData> entries, object savingContext)
        {
            Saved.AddRange(entries);

            return [.. entries.Select(_ => NoopEntry.Instance)];
        }

        public Task<IReadOnlyList<IAuditLogStoreEntry>> SaveAsync(
            IReadOnlyList<AuditLogEntryData> entries,
            object savingContext,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(Save(entries, savingContext));
        }
    }

    private sealed class NoopEntry : IAuditLogStoreEntry
    {
        public static readonly NoopEntry Instance = new();

        public void DiscardPendingChanges() { }

        public void ReleaseAfterCommit() { }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly List<AsyncServiceScope> _scopes = [];
        private ServiceProvider _provider = null!;

        public int Saves { get; set; }

        public CountingCapture Capture { get; } = new();

        public static async Task<Harness> CreateAsync(
            Action<IServiceCollection> configure,
            Action<HeadlessDbContextOptions>? configureOptions = null
        )
        {
            var harness = new Harness();
            await harness._connection.OpenAsync(AbortToken);
            var services = new ServiceCollection();
            services.AddLogging();
            services
                .AddHeadlessDbContext<OrdersDbContext>(
                    options =>
                        options
                            .UseSqlite(harness._connection)
                            .LogTo((id, _) => id == CoreEventId.SaveChangesStarting, _ => harness.Saves++),
                    configureOptions
                )
                .AddDomainEvents();

            // Every case observes captures through a deterministic stand-in, whatever the default capture audits.
            services.AddSingleton<IAuditChangeCapture>(harness.Capture);
            configure(services);
            harness._provider = services.BuildServiceProvider();
            await harness.CreateContext().Database.EnsureCreatedAsync(AbortToken);
            harness.Saves = 0;

            return harness;
        }

        public OrdersDbContext CreateContext()
        {
            var scope = _provider.CreateAsyncScope();
            _scopes.Add(scope);

            return scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
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
