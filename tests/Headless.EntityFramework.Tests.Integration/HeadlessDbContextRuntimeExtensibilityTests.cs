// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Abstractions;
using Headless.AuditLog;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.EntityFramework.Contexts.Processors;
using Headless.EntityFramework.Contexts.Runtime;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

// ReSharper disable EntityFramework.ModelValidation.UnlimitedStringLength
public sealed class HeadlessDbContextRuntimeExtensibilityTests : TestBase
{
    [Fact]
    public async Task should_be_idempotent_when_headless_db_context_runtime_initialize()
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
    public void should_replace_null_current_tenant_fallback_when_add_headless_db_context_services()
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
    public void should_preserve_custom_current_tenant_when_add_headless_db_context_services()
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
    public async Task should_run_custom_entry_processors_by_order_when_save_changes()
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
        await db.SaveChangesAsync(AbortToken);

        // then
        recorder.Entries.Should().Equal("early", "late");
    }

    [Fact]
    public async Task should_expose_current_tenant_on_entry_processor_context_when_save_changes()
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
        await db.SaveChangesAsync(AbortToken);

        // then
        recorder.Entries.Should().Contain("tenant:custom");
    }

    [Fact]
    public async Task should_use_default_processors_when_save_changes_entity_does_not_emit_messages()
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
        await db.SaveChangesAsync(AbortToken);

        // then - the default pipeline completes and the row round-trips
        var persisted = await db.BasicEntities.CountAsync(AbortToken);
        persisted.Should().Be(1);
    }

    [Fact]
    public async Task should_throw_when_save_changes_messages_are_emitted_without_dispatcher()
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
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*IDomainEventDispatcher*");
    }

    [Fact]
    public async Task should_throw_when_save_changes_integration_events_emitted_without_outbox_dispatcher()
    {
        // given — IDomainEventDispatcher is registered (so the AggregateRoot lifecycle domain events drained
        // by the first save are satisfied), but no IHeadlessOutboxDispatcher. The second save queues an
        // integration event on the tracked entity; collecting it must fail naming the missing dispatcher.
        var (provider, connection) = await _CreateProviderAsync(services =>
            services.AddScoped<IDomainEventDispatcher, RuntimeRecordingMessageDispatcher>()
        );
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "integration-emits" };

        db.Entities.Add(entity);
        await db.SaveChangesAsync(AbortToken);

        entity.EmitIntegrationEvent(new RuntimeDistributedMessage("needs-outbox"));

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*IHeadlessOutboxDispatcher*");
    }

    [Fact]
    public async Task should_name_add_domain_events_when_save_changes_domain_event_emitted_without_local_event_bus()
    {
        // given — the default pipeline emits lifecycle domain events for the tracked AggregateRoot, but
        // no IDomainEventDispatcher is registered. The guard message must point the consumer at the actionable
        // registration call (AddDomainEvents), not just the bus interface name.
        var (provider, connection) = await _CreateProviderAsync();
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "names-add-domain-events" };

        db.Entities.Add(entity);

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*AddDomainEvents*");
    }

    [Fact]
    public async Task should_name_add_integration_event_outbox_when_save_changes_integration_event_emitted_without_outbox_dispatcher()
    {
        // given — IDomainEventDispatcher is registered so the first save's lifecycle domain events drain, but no
        // IHeadlessOutboxDispatcher. Queuing an integration event on the tracked entity must fail with a
        // message naming the actionable registration call (AddIntegrationEventOutbox).
        var (provider, connection) = await _CreateProviderAsync(services =>
            services.AddScoped<IDomainEventDispatcher, RuntimeRecordingMessageDispatcher>()
        );
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "names-add-integration-outbox" };

        db.Entities.Add(entity);
        await db.SaveChangesAsync(AbortToken);

        entity.EmitIntegrationEvent(new RuntimeDistributedMessage("needs-outbox"));

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();
        assertion.WithMessage("*AddIntegrationEventOutbox*");
    }

    [Fact]
    public async Task should_not_throw_when_save_changes_aggregate_root_emits_no_events_and_no_buses_registered()
    {
        // given — an AggregateRoot is tracked and saved, but the lifecycle local-event processor is
        // removed so it emits zero domain events and (untouched) zero integration events. With neither
        // IDomainEventDispatcher nor IHeadlessOutboxDispatcher registered the guards must stay silent: emitting
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
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
        (await db.Entities.CountAsync(AbortToken)).Should().Be(1);
    }

    [Fact]
    public async Task should_use_registered_message_dispatcher_when_save_changes_messages_are_emitted()
    {
        // given
        var (provider, connection) = await _CreateProviderAsync(_AddRuntimeRecorder);
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<RuntimeRecordingMessageDispatcher>();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "emits" };

        db.Entities.Add(entity);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        // Flat domain events: AggregateRoot emits EntityCreated + EntityChanged on add.
        dispatcher.LocalEmitters.Should().HaveCount(2);
        dispatcher.DistributedEmitters.Should().BeEmpty();
    }

    [Fact]
    public async Task should_publish_messages_queued_on_unchanged_tracked_emitters_when_save_changes()
    {
        // given
        var (provider, connection) = await _CreateProviderAsync(_AddRuntimeRecorder);
        await using var _ = connection;
        await using var __ = provider;
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<RuntimeRecordingMessageDispatcher>();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var entity = new RuntimeEntity { Name = "emits-later" };

        db.Entities.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        db.Entry(entity).State.Should().Be(EntityState.Unchanged);
        dispatcher.LocalEmitters.Clear();
        dispatcher.DistributedEmitters.Clear();

        entity.EmitDomainEvent(new RuntimeLocalMessage("local-later"));
        entity.EmitIntegrationEvent(new RuntimeDistributedMessage("distributed-later"));

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        dispatcher
            .LocalEmitters.OfType<RuntimeLocalMessage>()
            .Should()
            .ContainSingle(x => x.UniqueId == "local-later");
        dispatcher
            .DistributedEmitters.OfType<RuntimeDistributedMessage>()
            .Should()
            .ContainSingle(x => x.UniqueId == "distributed-later");
    }

    [Fact]
    public async Task should_publish_messages_queued_by_custom_entry_processors_when_save_changes()
    {
        // given
        var (provider, connection) = await _CreateProviderAsync(
            _AddRuntimeRecorder,
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
        await db.SaveChangesAsync(AbortToken);

        // then
        dispatcher
            .LocalEmitters.OfType<RuntimeLocalMessage>()
            .Should()
            .ContainSingle(message => message.UniqueId == "custom-local");
        dispatcher
            .DistributedEmitters.OfType<RuntimeDistributedMessage>()
            .Should()
            .ContainSingle(message => message.UniqueId == "custom-distributed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_publish_domain_events_at_most_once_when_save_changes_execution_strategy_retries(
        bool synchronous
    )
    {
        // given — SQLite has no built-in retrying strategy, so we wire a one-shot retrying execution strategy
        // (ReplaceService<IExecutionStrategyFactory>) plus a SaveChanges interceptor that throws a marker
        // exception on its FIRST invocation and passes through on the second. The interceptor fires inside
        // baseSaveChanges, which the pipeline calls AFTER its domain-event publish loop. So attempt 1:
        // publish domain events -> baseSaveChanges throws the marker -> execution strategy classifies it as
        // transient and replays the whole operation. Attempt 2: the at-most-once guard skips the publish loop,
        // the interceptor passes through, the save commits. A correct guard fires each handler exactly once.
        var bus = new CountingDomainEventDispatcher();
        var interceptor = new OneShotTransientFailureInterceptor();
        var (provider, connection) = await _CreateProviderAsync(
            configureServices: services => services.AddSingleton<IDomainEventDispatcher>(bus),
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
        await _SaveAsync(db, synchronous);

        // then — the interceptor actually fired and the strategy replayed (guards against a silently-green
        // test where no retry happened at all).
        interceptor.InvocationCount.Should().Be(2, "the operation must be replayed once");

        // AggregateRoot.Add emits EntityCreated + EntityChanged = 2 domain events. Each handler must fire
        // exactly once across BOTH attempts — a re-fire on the replay would double the count to 4.
        bus.Occurrences.Select(occurrence => occurrence.EventId).Should().OnlyHaveUniqueItems();
        entity.GetDomainEvents().Should().BeEmpty();
        bus.PublishCount.Should().Be(2, "each domain event must be published exactly once despite the retry");

        // and the row is persisted (the save ultimately succeeded on the replayed attempt).
        (await db.Entities.CountAsync(AbortToken))
            .Should()
            .Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_drain_nested_occurrences_and_new_emitters_before_saving(bool synchronous)
    {
        var capture = new RecordingAuditCapture();
        var (provider, connection) = await _CreateProviderAsync(services =>
        {
            _AddRuntimeRecorder(services);
            services.AddSingleton<IAuditChangeCapture>(capture);
        });
        await using var connectionLifetime = connection;
        await using var providerLifetime = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<RuntimeRecordingMessageDispatcher>();
        var entity = new RuntimeEntity { Name = "parent" };
        entity.EmitDomainEvent(new RuntimeLocalMessage("root"));
        var root = entity.GetDomainEvents().Single();
        db.Entities.Add(entity);
        RuntimeEntity? added = null;
        dispatcher.OnLocal = payload =>
        {
            if (payload is RuntimeLocalMessage { UniqueId: "root" })
            {
                entity.EmitDomainEvent(new RuntimeLocalMessage("child"));
                entity.EmitIntegrationEvent(new RuntimeDistributedMessage("integration-child"));
                added = new RuntimeEntity { Name = "newly-tracked" };
                added.EmitDomainEvent(new RuntimeLocalMessage("new-emitter"));
                db.Entities.Add(added);
            }
        };

        await _SaveAsync(db, synchronous);

        dispatcher
            .LocalEmitters.OfType<RuntimeLocalMessage>()
            .Select(payload => payload.UniqueId)
            .Should()
            .Equal("root", "child", "new-emitter");
        dispatcher.LocalEmitters.OfType<EntityCreatedEventData<RuntimeEntity>>().Should().HaveCount(2);
        dispatcher.LocalEmitters.OfType<EntityChangedEventData<RuntimeEntity>>().Should().HaveCount(2);
        dispatcher.DistributedEmitters.Should().ContainSingle();
        var child = dispatcher.LocalOccurrences.Single(occurrence =>
            occurrence.Payload is RuntimeLocalMessage { UniqueId: "child" }
        );
        child.CausationId.Should().Be(root.EventId);
        child.CorrelationId.Should().Be(root.CorrelationId);
        entity.GetDomainEvents().Should().BeEmpty();
        entity.GetIntegrationEvents().Should().BeEmpty();
        added!.GetDomainEvents().Should().BeEmpty();
        (await db.Entities.CountAsync(AbortToken)).Should().Be(2);
        capture.CapturedNames.Should().BeEquivalentTo("parent", "newly-tracked");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_bound_recursive_emission_before_business_save(bool synchronous)
    {
        var (provider, connection) = await _CreateProviderAsync(_AddRuntimeRecorder);
        await using var connectionLifetime = connection;
        await using var providerLifetime = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<RuntimeRecordingMessageDispatcher>();
        var entity = new RuntimeEntity { Name = "recursive" };
        entity.EmitDomainEvent(new RuntimeLocalMessage("recursive"));
        db.Entities.Add(entity);
        dispatcher.OnLocal = payload =>
        {
            if (payload is RuntimeLocalMessage)
            {
                entity.EmitDomainEvent(new RuntimeLocalMessage("recursive"));
            }
        };

        var save = async () => await _SaveAsync(db, synchronous);
        (await save.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*1024*recursive*");
        (await db.Entities.CountAsync(AbortToken)).Should().Be(0);
        entity.GetDomainEvents().Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task should_complete_each_saved_batch_before_caller_transaction_outcome(
        bool synchronous,
        bool rollback
    )
    {
        var (provider, connection) = await _CreateProviderAsync(_AddRuntimeRecorder);
        await using var connectionLifetime = connection;
        await using var providerLifetime = provider;
        string firstId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<RuntimeRecordingMessageDispatcher>();
            await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);
            var first = new RuntimeEntity { Name = "first" };
            first.EmitDomainEvent(new RuntimeLocalMessage("application-command"));
            firstId = first.GetDomainEvents().Single().EventId;
            db.Entities.Add(first);
            await _SaveAsync(db, synchronous);
            first.GetDomainEvents().Should().BeEmpty();
            var countAfterFirstSave = dispatcher.LocalEmitters.Count;
            db.Entities.Add(new RuntimeEntity { Name = "second" });
            await _SaveAsync(db, synchronous);
            dispatcher.LocalEmitters.Count.Should().Be(countAfterFirstSave + 2);
            if (rollback)
            {
                await transaction.RollbackAsync(AbortToken);
            }
            else
            {
                await transaction.CommitAsync(AbortToken);
            }
        }

        // Recovery observes durable state through a fresh context and abandons the prior aggregate graph.
        await using var freshScope = provider.CreateAsyncScope();
        var fresh = freshScope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        (await fresh.Entities.CountAsync(AbortToken)).Should().Be(rollback ? 0 : 2);
        var replay = new RuntimeEntity { Name = "replayed-command" };
        replay.EmitDomainEvent(new RuntimeLocalMessage("application-command"));
        replay.GetDomainEvents().Single().EventId.Should().NotBe(firstId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_leave_occurrences_added_after_drain_pending_for_next_save(bool synchronous)
    {
        var entity = new RuntimeEntity { Name = "late-occurrence" };
        var interceptor = new LateOccurrenceInterceptor(entity);
        var (provider, connection) = await _CreateProviderAsync(
            _AddRuntimeRecorder,
            configureDbContext: options => options.AddInterceptors(interceptor)
        );
        await using var connectionLifetime = connection;
        await using var providerLifetime = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<RuntimeRecordingMessageDispatcher>();
        db.Entities.Add(entity);

        await _SaveAsync(db, synchronous);
        var remaining = entity.GetDomainEvents().Should().ContainSingle().Subject;
        remaining.Payload.Should().BeOfType<RuntimeLocalMessage>().Which.UniqueId.Should().Be("after-drain");
        dispatcher.LocalEmitters.OfType<RuntimeLocalMessage>().Should().BeEmpty();
        await _SaveAsync(db, synchronous);
        entity.GetDomainEvents().Should().BeEmpty();
        dispatcher.LocalOccurrences.Should().Contain(occurrence => occurrence.EventId == remaining.EventId);
    }

    private sealed class LateOccurrenceInterceptor(RuntimeEntity entity) : ISaveChangesInterceptor
    {
        private bool _emitted;

        private void _EmitOnce()
        {
            if (!_emitted)
            {
                _emitted = true;
                entity.EmitDomainEvent(new RuntimeLocalMessage("after-drain"));
            }
        }

        public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            _EmitOnce();
            return result;
        }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            _EmitOnce();
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_honor_removed_collector_inside_caller_transaction(bool synchronous)
    {
        var (provider, connection) = await _CreateProviderAsync(
            _AddRuntimeRecorder,
            options => options.RemoveSaveEntryProcessor<HeadlessMessageCollectorSaveEntryProcessor>()
        );
        await using var connectionLifetime = connection;
        await using var providerLifetime = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<RuntimeRecordingMessageDispatcher>();
        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);
        var entity = new RuntimeEntity { Name = "collector-disabled" };
        db.Entities.Add(entity);
        await _SaveAsync(db, synchronous);
        dispatcher.LocalEmitters.Should().BeEmpty();
        entity.GetDomainEvents().Should().NotBeEmpty();
        await transaction.CommitAsync(AbortToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_retry_business_write_after_transient_outbox_failure_without_repeating_local_drain(
        bool synchronous
    )
    {
        var (provider, connection) = await _CreateProviderAsync(
            _AddRuntimeRecorder,
            configureDbContext: options =>
                options.ReplaceService<IExecutionStrategyFactory, OneShotRetryExecutionStrategyFactory>()
        );
        await using var connectionLifetime = connection;
        await using var providerLifetime = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<RuntimeRecordingMessageDispatcher>();
        var entity = new RuntimeEntity { Name = "post-save-retry" };
        entity.EmitIntegrationEvent(new RuntimeDistributedMessage("outbox"));
        db.Entities.Add(entity);
        var attempts = 0;
        dispatcher.OnDistributed = () =>
        {
            if (++attempts == 1)
            {
                throw new TransientMarkerException();
            }
        };

        await _SaveAsync(db, synchronous);
        attempts.Should().Be(2);
        dispatcher.LocalEmitters.Should().HaveCount(2);
        (await db.Entities.CountAsync(AbortToken)).Should().Be(1);
        entity.GetIntegrationEvents().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_surface_unknown_commit_outcome_without_retrying(bool synchronous)
    {
        var interceptor = new UnknownCommitInterceptor();
        var (provider, connection) = await _CreateProviderAsync(
            _AddRuntimeRecorder,
            configureDbContext: options =>
                options
                    .ReplaceService<IExecutionStrategyFactory, OneShotRetryExecutionStrategyFactory>()
                    .AddInterceptors(interceptor)
        );
        await using var connectionLifetime = connection;
        await using var providerLifetime = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<RuntimeRecordingMessageDispatcher>();
        db.Entities.Add(new RuntimeEntity { Name = "commit-result-lost" });
        interceptor.Enabled = true;

        var save = async () => await _SaveAsync(db, synchronous);
        await save.Should().ThrowAsync<TransientMarkerException>();
        interceptor.CommitCalls.Should().Be(1);
        dispatcher.LocalEmitters.Should().HaveCount(2);
        (await db.Entities.CountAsync(AbortToken))
            .Should()
            .Be(1, "the database committed even though the application did not receive success");
    }

    private sealed class UnknownCommitInterceptor : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }
        public int CommitCalls { get; private set; }

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        {
            if (Enabled)
            {
                CommitCalls++;
                throw new TransientMarkerException();
            }
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            TransactionCommitted(transaction, eventData);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAuditCapture : IAuditChangeCapture
    {
        public string[] CapturedNames { get; private set; } = [];

        public IReadOnlyList<AuditLogEntryData> CaptureChanges(
            IEnumerable<object> entries,
            string? userId,
            string? accountId,
            string? tenantId,
            string? correlationId,
            DateTimeOffset timestamp
        )
        {
            CapturedNames = entries
                .OfType<EntityEntry>()
                .Select(entry => entry.Entity)
                .OfType<RuntimeEntity>()
                .Select(entity => entity.Name)
                .ToArray();
            return [];
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_dispatch_shared_occurrence_once_and_clear_every_captured_emitter(bool synchronous)
    {
        var (provider, connection) = await _CreateProviderAsync(_AddRuntimeRecorder);
        await using var connectionLifetime = connection;
        await using var providerLifetime = provider;
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<RuntimeRecordingMessageDispatcher>();
        var first = new RuntimeEntity { Name = "first-emitter" };
        var second = new RuntimeEntity { Name = "second-emitter" };
        var domain = EventContext.Capture<object>(new RuntimeLocalMessage("shared"));
        var integration = EventContext.Capture<object>(new RuntimeDistributedMessage("shared"));
        ((IDomainEventEmitter)first).AddDomainEvent(domain);
        ((IDomainEventEmitter)second).AddDomainEvent(domain);
        ((IIntegrationEventEmitter)first).AddIntegrationEvent(integration);
        ((IIntegrationEventEmitter)second).AddIntegrationEvent(integration);
        db.Entities.AddRange(first, second);

        await _SaveAsync(db, synchronous);

        dispatcher.LocalEmitters.OfType<RuntimeLocalMessage>().Should().ContainSingle();
        dispatcher.DistributedEmitters.Should().ContainSingle();
        first.GetDomainEvents().Should().BeEmpty();
        second.GetDomainEvents().Should().BeEmpty();
        first.GetIntegrationEvents().Should().BeEmpty();
        second.GetIntegrationEvents().Should().BeEmpty();
    }

    private static Task<int> _SaveAsync(RuntimeTestDbContext db, bool synchronous)
    {
#pragma warning disable MA0045 // Explicit synchronous SaveChanges conformance case.
        return synchronous ? Task.FromResult(db.SaveChanges()) : db.SaveChangesAsync(AbortToken);
#pragma warning restore MA0045
    }

    private static async Task<(ServiceProvider Provider, SqliteConnection Connection)> _CreateProviderAsync(
        Action<IServiceCollection>? configureServices = null,
        Action<HeadlessDbContextOptions>? configureHeadlessOptions = null,
        Action<DbContextOptionsBuilder>? configureDbContext = null
    )
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);

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
        await scope.ServiceProvider.GetRequiredService<RuntimeTestDbContext>().Database.EnsureCreatedAsync(AbortToken);

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

    private sealed class RuntimeRecordingMessageDispatcher : IDomainEventDispatcher, IHeadlessOutboxDispatcher
    {
        public List<object> LocalEmitters { get; } = [];
        public List<EventContext<object>> LocalOccurrences { get; } = [];
        public Action<object>? OnLocal { get; set; }
        public Action? OnDistributed { get; set; }

        public List<object> DistributedEmitters { get; } = [];

        public ValueTask DispatchAsync<TPayload>(
            EventContext<TPayload> context,
            CancellationToken cancellationToken = default
        )
            where TPayload : class
        {
            using var emission = EventEmissionScope.Begin(context);
            LocalOccurrences.Add(
                new(context.Payload, context.EventId, context.CorrelationId, context.CausationId, context.TenantId)
            );
            OnLocal?.Invoke(context.Payload);
            LocalEmitters.Add(context.Payload);
            return ValueTask.CompletedTask;
        }

        public Task DispatchAsync(
            IReadOnlyList<EventContext<object>> integrationEvents,
            CancellationToken cancellationToken = default
        )
        {
            OnDistributed?.Invoke();
            DistributedEmitters.AddRange(integrationEvents.Select(occurrence => occurrence.Payload));
            return Task.CompletedTask;
        }

        public void Dispatch(IReadOnlyList<EventContext<object>> integrationEvents)
        {
            OnDistributed?.Invoke();
            DistributedEmitters.AddRange(integrationEvents.Select(occurrence => occurrence.Payload));
        }
    }

    private static void _AddRuntimeRecorder(IServiceCollection services)
    {
        services.AddScoped<RuntimeRecordingMessageDispatcher>();
        services.AddScoped<IDomainEventDispatcher>(sp => sp.GetRequiredService<RuntimeRecordingMessageDispatcher>());
        services.AddScoped<IHeadlessOutboxDispatcher>(sp => sp.GetRequiredService<RuntimeRecordingMessageDispatcher>());
    }

    // Counts every domain-event publish so the test can prove handlers fire exactly once across the retry.
    private sealed class CountingDomainEventDispatcher : IDomainEventDispatcher
    {
        private int _publishCount;

        public int PublishCount => _publishCount;
        public List<EventContext<object>> Occurrences { get; } = [];

        public ValueTask DispatchAsync<TPayload>(
            EventContext<TPayload> context,
            CancellationToken cancellationToken = default
        )
            where TPayload : class
        {
            Occurrences.Add(
                new(context.Payload, context.EventId, context.CorrelationId, context.CausationId, context.TenantId)
            );
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
        protected override bool ShouldRetryOn(Exception exception)
        {
            return exception is TransientMarkerException;
        }
    }

    private sealed class OneShotRetryExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create()
        {
            return new OneShotRetryExecutionStrategy(dependencies);
        }
    }

    private sealed record RuntimeLocalMessage(string UniqueId);

    private sealed record RuntimeDistributedMessage(string UniqueId);

    private sealed class RuntimeQueuedMessageSaveEntryProcessor : IHeadlessSaveEntryProcessor
    {
        public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
        {
            if (entry is not { Entity: RuntimeEntity entity, State: EntityState.Added })
            {
                return;
            }

            entity.EmitDomainEvent(new RuntimeLocalMessage("custom-local"));
            entity.EmitIntegrationEvent(new RuntimeDistributedMessage("custom-distributed"));
        }
    }

    private sealed class RuntimeCustomCurrentTenant : ICurrentTenant
    {
        public bool IsAvailable => true;

        public string Id => "custom";

        public string Name => "Custom";

        public IDisposable Change(string? id, string? name = null)
        {
            return new RuntimeCurrentTenantScope();
        }
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

        public IReadOnlyList<object> GetKeys()
        {
            return [Id];
        }
    }

    private sealed class RuntimeEntity : AggregateRoot, IEntity<Guid>
    {
        public Guid Id { get; private init; }

        public required string Name { get; init; }

        // Domain behavior that raises events through the encapsulated (protected) aggregate mutators.
        public void EmitDomainEvent(object domainEvent)
        {
            AddDomainEvent(domainEvent);
        }

        public void EmitIntegrationEvent(object integrationEvent)
        {
            AddIntegrationEvent(integrationEvent);
        }

        public override IReadOnlyList<object> GetKeys()
        {
            return [Id];
        }
    }
}
