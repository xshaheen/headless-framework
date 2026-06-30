// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.EntityFramework.Contexts.Processors;
using Headless.EntityFramework.Contexts.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

// ReSharper disable EntityFramework.ModelValidation.UnlimitedStringLength
public sealed class HeadlessDbContextRuntimeExtensibilityTests
{
    [Fact]
    public async Task headless_db_context_runtime_initialize_should_be_idempotent()
    {
        // given — a DbContext-backed runtime that has already been initialized through the DbContext
        // constructor. Calling Initialize() again must be a no-op (no double-subscription of the
        // ChangeTracker handlers, no observable state change).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContextServices();
        services.AddDbContext<RuntimeTestDbContext>(o =>
        {
            o.UseSqlite(new SqliteConnection("Filename=:memory:"));
            o.AddHeadlessExtension();
        });

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();

        // when — second Initialize() must not throw.
        var runtimeServices = scope.ServiceProvider.GetRequiredService<HeadlessDbContextServices>();
        await using var runtime = new HeadlessDbContextRuntime(db, runtimeServices);
        runtime.Initialize();

        // then
        var act = runtime.Initialize;
        act.Should().NotThrow();
    }

    [Fact]
    public void add_headless_db_context_services_should_replace_null_current_tenant_fallback()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant, NullCurrentTenant>();

        // when
        services.AddHeadlessDbContextServices();
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<ICurrentTenant>().Should().BeOfType<CurrentTenant>();
    }

    [Fact]
    public void add_headless_db_context_services_should_preserve_custom_current_tenant()
    {
        // given
        var customTenant = new RuntimeCustomCurrentTenant();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(customTenant);

        // when
        services.AddHeadlessDbContextServices();
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<ICurrentTenant>().Should().BeSameAs(customTenant);
    }

    [Fact]
    public async Task save_changes_should_run_custom_entry_processors_by_order()
    {
        // given
        var (provider, connection) = await _CreateProviderAsync();
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var recorder = scope.ServiceProvider.GetRequiredService<ProcessorOrderRecorder>();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeBasicEntity { Name = "ordered" };

        db.BasicEntities.Add(entity);

        // when
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then
        recorder.Entries.Should().Equal("early", "late");
    }

    [Fact]
    public async Task save_changes_should_expose_current_tenant_on_entry_processor_context()
    {
        // given
        var (provider, connection) = await _CreateProviderAsync(
            services => services.AddSingleton<ICurrentTenant>(new RuntimeCustomCurrentTenant()),
            options => options.AddSaveEntryProcessor<TenantRecordingSaveEntryProcessor>(ServiceLifetime.Singleton)
        );
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var recorder = scope.ServiceProvider.GetRequiredService<ProcessorOrderRecorder>();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeBasicEntity { Name = "tenant-context" };

        db.BasicEntities.Add(entity);

        // when
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then
        recorder.Entries.Should().Contain("tenant:custom");
    }

    [Fact]
    public async Task save_changes_should_use_default_processors_when_entity_does_not_emit_messages()
    {
        // given
        var (provider, connection) = await _CreateProviderAsync();
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeBasicEntity { Name = "defaults" };

        // the framework value generator stamps the key as the entity is tracked, before SaveChanges
        db.BasicEntities.Add(entity);
        entity.Id.Should().NotBe(Guid.Empty);

        // when - no custom processors and no message dispatcher are registered
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then - the default pipeline completes and the row round-trips
        var persisted = await db.BasicEntities.CountAsync(TestContext.Current.CancellationToken);
        persisted.Should().Be(1);
    }

    [Fact]
    public async Task save_changes_should_throw_when_messages_are_emitted_without_dispatcher()
    {
        // given
        var (provider, connection) = await _CreateProviderAsync();
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "emits" };

        db.Entities.Add(entity);

        // when
        var act = async () => await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*ILocalEventBus*");
    }

    [Fact]
    public async Task save_changes_should_throw_when_integration_events_emitted_without_outbox_dispatcher()
    {
        // given — ILocalEventBus is registered (so the AggregateRoot lifecycle domain events drained
        // by the first save are satisfied), but no IHeadlessOutboxDispatcher. The second save queues an
        // integration event on the tracked entity; collecting it must fail naming the missing dispatcher.
        var (provider, connection) = await _CreateProviderAsync(services =>
            services.AddScoped<ILocalEventBus, RuntimeRecordingMessageDispatcher>()
        );
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "integration-emits" };

        db.Entities.Add(entity);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        entity.AddIntegrationEvent(new RuntimeDistributedMessage("needs-outbox"));

        // when
        var act = async () => await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*IHeadlessOutboxDispatcher*");
    }

    [Fact]
    public async Task save_changes_should_name_add_domain_events_when_domain_event_emitted_without_local_event_bus()
    {
        // given — the default pipeline emits lifecycle domain events for the tracked AggregateRoot, but
        // no ILocalEventBus is registered. The guard message must point the consumer at the actionable
        // registration call (AddDomainEvents), not just the bus interface name.
        var (provider, connection) = await _CreateProviderAsync();
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "names-add-domain-events" };

        db.Entities.Add(entity);

        // when
        var act = async () => await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*AddDomainEvents*");
    }

    [Fact]
    public async Task save_changes_should_name_add_integration_event_outbox_when_integration_event_emitted_without_outbox_dispatcher()
    {
        // given — ILocalEventBus is registered so the first save's lifecycle domain events drain, but no
        // IHeadlessOutboxDispatcher. Queuing an integration event on the tracked entity must fail with a
        // message naming the actionable registration call (AddIntegrationEventOutbox).
        var (provider, connection) = await _CreateProviderAsync(services =>
            services.AddScoped<ILocalEventBus, RuntimeRecordingMessageDispatcher>()
        );
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "names-add-integration-outbox" };

        db.Entities.Add(entity);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        entity.AddIntegrationEvent(new RuntimeDistributedMessage("needs-outbox"));

        // when
        var act = async () => await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*AddIntegrationEventOutbox*");
    }

    [Fact]
    public async Task save_changes_should_not_throw_when_aggregate_root_emits_no_events_and_no_buses_registered()
    {
        // given — an AggregateRoot is tracked and saved, but the lifecycle local-event processor is
        // removed so it emits zero domain events and (untouched) zero integration events. With neither
        // ILocalEventBus nor IHeadlessOutboxDispatcher registered the guards must stay silent: emitting
        // nothing is the common case and must never require either bus.
        var (provider, connection) = await _CreateProviderAsync(configureHeadlessOptions: options =>
            options.RemoveSaveEntryProcessor<HeadlessLocalEventSaveEntryProcessor>()
        );
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "emits-nothing" };

        db.Entities.Add(entity);

        // when
        var act = async () => await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then
        await act.Should().NotThrowAsync();
        (await db.Entities.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task save_changes_should_use_registered_message_dispatcher_when_messages_are_emitted()
    {
        // given
        var (provider, connection) = await _CreateProviderAsync(services => _AddRuntimeRecorder(services));
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<RuntimeRecordingMessageDispatcher>();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "emits" };

        db.Entities.Add(entity);

        // when
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then
        // Flat domain events: AggregateRoot emits EntityCreated + EntityChanged on add.
        dispatcher.LocalEmitters.Should().HaveCount(2);
        dispatcher.DistributedEmitters.Should().BeEmpty();
    }

    [Fact]
    public async Task save_changes_should_publish_messages_queued_on_unchanged_tracked_emitters()
    {
        // given
        var (provider, connection) = await _CreateProviderAsync(services => _AddRuntimeRecorder(services));
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<RuntimeRecordingMessageDispatcher>();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "emits-later" };

        db.Entities.Add(entity);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.Entry(entity).State.Should().Be(EntityState.Unchanged);
        dispatcher.LocalEmitters.Clear();
        dispatcher.DistributedEmitters.Clear();

        entity.AddDomainEvent(new RuntimeLocalMessage("local-later"));
        entity.AddIntegrationEvent(new RuntimeDistributedMessage("distributed-later"));

        // when
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then
        dispatcher.LocalEmitters.Should().ContainSingle(x => x.UniqueId == "local-later");
        dispatcher.DistributedEmitters.Should().ContainSingle(x => x.UniqueId == "distributed-later");
    }

    [Fact]
    public async Task save_changes_should_publish_messages_queued_by_custom_entry_processors()
    {
        // given
        var (provider, connection) = await _CreateProviderAsync(
            services => _AddRuntimeRecorder(services),
            options => options.AddSaveEntryProcessor<RuntimeQueuedMessageSaveEntryProcessor>(ServiceLifetime.Singleton)
        );
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<RuntimeRecordingMessageDispatcher>();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "processor-emits" };

        db.Entities.Add(entity);

        // when
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then
        dispatcher.LocalEmitters.Should().ContainSingle(message => message.UniqueId == "custom-local");
        dispatcher.DistributedEmitters.Should().ContainSingle(message => message.UniqueId == "custom-distributed");
    }

    [Fact]
    public async Task save_changes_should_publish_domain_events_at_most_once_when_execution_strategy_retries()
    {
        // given — SQLite has no built-in retrying strategy, so we wire a one-shot retrying execution strategy
        // (ReplaceService<IExecutionStrategyFactory>) plus a SaveChanges interceptor that throws a marker
        // exception on its FIRST invocation and passes through on the second. The interceptor fires inside
        // baseSaveChanges, which the pipeline calls AFTER its domain-event publish loop. So attempt 1:
        // publish domain events -> baseSaveChanges throws the marker -> execution strategy classifies it as
        // transient and replays the whole operation. Attempt 2: the at-most-once guard skips the publish loop,
        // the interceptor passes through, the save commits. A correct guard fires each handler exactly once.
        var bus = new CountingLocalEventBus();
        var interceptor = new OneShotTransientFailureInterceptor();
        var (provider, connection) = await _CreateProviderAsync(
            configureServices: services => services.AddSingleton<ILocalEventBus>(bus),
            configureDbContext: options =>
                options
                    .ReplaceService<IExecutionStrategyFactory, OneShotRetryExecutionStrategyFactory>()
                    .AddInterceptors(interceptor)
        );
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "retries-once" };

        db.Entities.Add(entity);

        // when
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // then — the interceptor actually fired and the strategy replayed (guards against a silently-green
        // test where no retry happened at all).
        interceptor.InvocationCount.Should().Be(2, "the operation must be replayed once");

        // AggregateRoot.Add emits EntityCreated + EntityChanged = 2 domain events. Each handler must fire
        // exactly once across BOTH attempts — a re-fire on the replay would double the count to 4.
        bus.PublishCount.Should().Be(2, "each domain event must be published exactly once despite the retry");

        // and the row is persisted (the save ultimately succeeded on the replayed attempt).
        (await db.Entities.CountAsync(TestContext.Current.CancellationToken))
            .Should()
            .Be(1);
    }

    private static async Task<(ServiceProvider Provider, SqliteConnection Connection)> _CreateProviderAsync(
        Action<IServiceCollection>? configureServices = null,
        Action<HeadlessDbContextOptions>? configureHeadlessOptions = null,
        Action<DbContextOptionsBuilder>? configureDbContext = null
    )
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(connection);
        // Recorder lifetime must match the processor lifetimes (singleton) so test queries observe
        // the same instance the processors write to.
        services.AddSingleton<ProcessorOrderRecorder>();
        services.AddHeadlessDbContextServices(options =>
        {
            options.AddSaveEntryProcessor<EarlyRecordingSaveEntryProcessor>(ServiceLifetime.Singleton);
            options.AddSaveEntryProcessor<LateRecordingSaveEntryProcessor>(ServiceLifetime.Singleton);
            configureHeadlessOptions?.Invoke(options);
        });
        configureServices?.Invoke(services);
        services.AddDbContext<RuntimeTestDbContext>(options =>
        {
            options.UseSqlite(connection).AddHeadlessExtension();
            configureDbContext?.Invoke(options);
        });

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<RuntimeTestDbContext>()
            .Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        return (provider, connection);
    }

    private sealed class ProcessorOrderRecorder
    {
        public List<string> Entries { get; } = [];
    }

    private sealed class EarlyRecordingSaveEntryProcessor(ProcessorOrderRecorder recorder) : IHeadlessSaveEntryProcessor
    {
        public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
        {
            if (entry is { Entity: RuntimeBasicEntity, State: EntityState.Added })
            {
                recorder.Entries.Add("early");
            }
        }
    }

    private sealed class LateRecordingSaveEntryProcessor(ProcessorOrderRecorder recorder) : IHeadlessSaveEntryProcessor
    {
        public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
        {
            if (entry is { Entity: RuntimeBasicEntity, State: EntityState.Added })
            {
                recorder.Entries.Add("late");
            }
        }
    }

    private sealed class TenantRecordingSaveEntryProcessor(ProcessorOrderRecorder recorder)
        : IHeadlessSaveEntryProcessor
    {
        public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
        {
            if (entry is { Entity: RuntimeBasicEntity, State: EntityState.Added })
            {
                recorder.Entries.Add($"tenant:{context.TenantId}");
            }
        }
    }

    private sealed class RuntimeRecordingMessageDispatcher : ILocalEventBus, IHeadlessOutboxDispatcher
    {
        public List<IDomainEvent> LocalEmitters { get; } = [];

        public List<IIntegrationEvent> DistributedEmitters { get; } = [];

        public void Publish<T>(T domainEvent)
            where T : class, IDomainEvent => LocalEmitters.Add(domainEvent);

        public void Publish(IDomainEvent domainEvent) => LocalEmitters.Add(domainEvent);

        public ValueTask PublishAsync<T>(T domainEvent, CancellationToken cancellationToken = default)
            where T : class, IDomainEvent
        {
            LocalEmitters.Add(domainEvent);
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            LocalEmitters.Add(domainEvent);
            return ValueTask.CompletedTask;
        }

        public Task DispatchAsync(
            IReadOnlyList<IIntegrationEvent> integrationEvents,
            CancellationToken cancellationToken
        )
        {
            DistributedEmitters.AddRange(integrationEvents);
            return Task.CompletedTask;
        }

        public void Dispatch(IReadOnlyList<IIntegrationEvent> integrationEvents)
        {
            DistributedEmitters.AddRange(integrationEvents);
        }
    }

    private static IServiceCollection _AddRuntimeRecorder(IServiceCollection services)
    {
        services.AddScoped<RuntimeRecordingMessageDispatcher>();
        services.AddScoped<ILocalEventBus>(sp => sp.GetRequiredService<RuntimeRecordingMessageDispatcher>());
        services.AddScoped<IHeadlessOutboxDispatcher>(sp => sp.GetRequiredService<RuntimeRecordingMessageDispatcher>());

        return services;
    }

    // Counts every domain-event publish so the test can prove handlers fire exactly once across the retry.
    private sealed class CountingLocalEventBus : ILocalEventBus
    {
        private int _publishCount;

        public int PublishCount => _publishCount;

        public void Publish<T>(T domainEvent)
            where T : class, IDomainEvent => Interlocked.Increment(ref _publishCount);

        public void Publish(IDomainEvent domainEvent) => Interlocked.Increment(ref _publishCount);

        public ValueTask PublishAsync<T>(T domainEvent, CancellationToken cancellationToken = default)
            where T : class, IDomainEvent
        {
            Interlocked.Increment(ref _publishCount);
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _publishCount);
            return ValueTask.CompletedTask;
        }
    }

    // Marker exception the one-shot strategy classifies as transient.
    private sealed class TransientMarkerException() : Exception("Simulated transient save fault.");

    // SaveChanges interceptor that throws the transient marker on its FIRST invocation and passes through on
    // the second. Because the pipeline runs its domain-event publish loop BEFORE baseSaveChanges (where this
    // interceptor fires), the throw forces a real replay AFTER the events have already published on attempt 1.
    private sealed class OneShotTransientFailureInterceptor : ISaveChangesInterceptor
    {
        private int _invocationCount;

        public int InvocationCount => _invocationCount;

        public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (Interlocked.Increment(ref _invocationCount) == 1)
            {
                throw new TransientMarkerException();
            }

            return result;
        }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (Interlocked.Increment(ref _invocationCount) == 1)
            {
                throw new TransientMarkerException();
            }

            return new ValueTask<InterceptionResult<int>>(result);
        }
    }

    // Retries the wrapped operation at most once, treating TransientMarkerException as transient. Zero retry
    // delay keeps the test fast and deterministic.
    private sealed class OneShotRetryExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TransientMarkerException;
    }

    private sealed class OneShotRetryExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new OneShotRetryExecutionStrategy(dependencies);
    }

    private sealed record RuntimeLocalMessage(string UniqueId) : IDomainEvent;

    private sealed record RuntimeDistributedMessage(string UniqueId) : IIntegrationEvent;

    private sealed class RuntimeQueuedMessageSaveEntryProcessor : IHeadlessSaveEntryProcessor
    {
        public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
        {
            if (entry is not { Entity: RuntimeEntity entity, State: EntityState.Added })
            {
                return;
            }

            entity.AddDomainEvent(new RuntimeLocalMessage("custom-local"));
            entity.AddIntegrationEvent(new RuntimeDistributedMessage("custom-distributed"));
        }
    }

    private sealed class RuntimeCustomCurrentTenant : ICurrentTenant
    {
        public bool IsAvailable => true;

        public string Id => "custom";

        public string Name => "Custom";

        public IDisposable Change(string? id, string? name = null) => new RuntimeCurrentTenantScope();
    }

    private sealed class RuntimeCurrentTenantScope : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class RuntimeTestDbContext(HeadlessDbContextServices services, DbContextOptions options)
        : HeadlessDbContext(services, options)
    {
        public DbSet<RuntimeEntity> Entities => Set<RuntimeEntity>();

        public DbSet<RuntimeBasicEntity> BasicEntities => Set<RuntimeBasicEntity>();

        public override string DefaultSchema => "";
    }

    private sealed class RuntimeBasicEntity : IEntity<Guid>
    {
        public Guid Id { get; private init; }

        public required string Name { get; init; }

        public IReadOnlyList<object> GetKeys() => [Id];
    }

    private sealed class RuntimeEntity : AggregateRoot, IEntity<Guid>
    {
        public Guid Id { get; private init; }

        public required string Name { get; init; }

        public override IReadOnlyList<object> GetKeys() => [Id];
    }
}
