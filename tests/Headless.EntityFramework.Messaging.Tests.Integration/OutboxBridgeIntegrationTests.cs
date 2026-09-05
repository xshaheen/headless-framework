// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Headless.Abstractions;
using Headless.CommitCoordination;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Testing.Tests;
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
public sealed partial class OutboxBridgeIntegrationTests(OutboxBridgeTestFixture fixture) : TestBase
{
    protected override async ValueTask DisposeAsyncCore()
    {
        try
        {
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(AbortToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                TRUNCATE TABLE messaging."published" CASCADE;
                TRUNCATE TABLE messaging."received" CASCADE;
                TRUNCATE TABLE "Orders" CASCADE;
                TRUNCATE TABLE "DeadlineReceipts" CASCADE;
                """;
            await command.ExecuteNonQueryAsync(AbortToken);
        }
        catch (PostgresException)
        {
            // Schema/table might not exist yet
        }

        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_write_one_outbox_row_when_save_emitting_an_integration_event()
    {
        // given
        const string marker = "evt-single";
        await using var provider = await _BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        var order = new OrderEntity { Name = "ship" };
        using (EventEmissionScope.Begin(new EventEmissionContext("business-root", "domain-parent", "tenant-a")))
        {
            order.EmitIntegrationEvent(new OrderShipped($"{marker}-1"));
        }
        var occurrence = order.GetIntegrationEvents().Single();
        db.Orders.Add(order);

        // when — no ambient transaction: the pipeline opens one, writes business + outbox rows, commits.
        await db.SaveChangesAsync(AbortToken);

        // then
        (await _CountPublishedContainingAsync(marker))
            .Should()
            .Be(1);
        var row = (await _ReadPublishedAsync(provider, marker)).Single();
        row.Id.Should().Be(occurrence.EventId);
        var message = row.Message;
        message.Should().NotBeNull();
        message.Headers[Headers.MessageId].Should().Be(occurrence.EventId);
        message.Headers[Headers.CorrelationId].Should().Be(occurrence.CorrelationId);
        message.Headers[Headers.CausationId].Should().Be(occurrence.CausationId);
        message.Headers[Headers.TenantId].Should().Be(occurrence.TenantId);
    }

    [Fact]
    public async Task should_route_each_to_its_own_overload_when_save_emitting_multiple_concrete_event_types()
    {
        // given
        const string marker = "evt-multi";
        await using var provider = await _BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        var order = new OrderEntity { Name = "multi" };
        order.EmitIntegrationEvent(new OrderShipped($"{marker}-shipped"));
        order.EmitIntegrationEvent(new OrderInvoiced($"{marker}-invoiced"));
        db.Orders.Add(order);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then — both concrete types persisted (compiled invoker resolved each closed generic correctly)
        (await _CountPublishedContainingAsync($"{marker}-shipped"))
            .Should()
            .Be(1);
        (await _CountPublishedContainingAsync($"{marker}-invoiced")).Should().Be(1);
    }

    [Fact]
    public async Task should_write_outbox_rows_identically_to_async_when_sync_save()
    {
        // given
        const string marker = "evt-sync";
        await using var provider = await _BuildProviderAsync();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        var order = new OrderEntity { Name = "sync" };
        order.EmitIntegrationEvent(new OrderShipped($"{marker}-1"));
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
    public async Task should_fail_loud_when_save_emitting_events_under_a_consumer_opened_plain_transaction()
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
        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);
        var order = new OrderEntity { Name = "consumer-plain" };
        order.EmitIntegrationEvent(new OrderShipped($"{marker}-1"));
        db.Orders.Add(order);

        // when — save under the un-enlisted consumer transaction.
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then — fails loud with an actionable wiring error and writes no outbox row.
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*not enlisted in commit coordination*"
        );
        await transaction.RollbackAsync(AbortToken);
        (await _CountPublishedContainingAsync(marker)).Should().Be(0);
    }

    [Fact]
    public async Task should_dispatch_the_event_atomically_when_coordinated_transaction_wrapping_a_save()
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
                order.EmitIntegrationEvent(new OrderShipped($"{marker}-1"));
                ctx.Orders.Add(order);
                await ctx.SaveChangesAsync(ct);
            },
            cancellationToken: AbortToken
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
        order.EmitIntegrationEvent(new OrderShipped($"{marker}-1"));
        db.Orders.Add(order);

        // when — pipeline opens the coordinated transaction, writes business + outbox rows, commits atomically.
        await db.SaveChangesAsync(AbortToken);

        // then — the outbox row is durable post-commit, alongside the committed business row.
        (await _CountPublishedContainingAsync(marker))
            .Should()
            .Be(1);
        (await _CountOrdersAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_discard_the_outbox_row_when_enlisted_publish_rolled_back()
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
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);

        await using (db.Database.EnlistCommitCoordination(transaction, scope.ServiceProvider, AbortToken))
        {
            // when — publish enlists the row inside the transaction, then the consumer rolls back.
            await bus.PublishAsync(
                new OrderShipped($"{marker}-1"),
                new PublishOptions { DeliveryMode = DeliveryMode.Durable },
                AbortToken
            );

            await transaction.RollbackAsync(AbortToken);
        }

        // then — the enlisted row rolled back with the transaction.
        (await _CountPublishedContainingAsync(marker))
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task should_persist_the_outbox_row_atomically_when_enlisted_publish_committed()
    {
        // given — same enlist seam, but commit. Proves the in-tx write path (not the autonomous fallback): the row
        // is only visible after commit and survives. Paired with the rollback test, this pins atomic enlistment.
        const string marker = "evt-enlist-commit";
        await using var provider = await _BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);

        await using (db.Database.EnlistCommitCoordination(transaction, scope.ServiceProvider, AbortToken))
        {
            await bus.PublishAsync(
                new OrderShipped($"{marker}-1"),
                new PublishOptions { DeliveryMode = DeliveryMode.Durable },
                AbortToken
            );

            // when — commit the enlisting transaction.
            await transaction.CommitAsync(AbortToken);
        }

        // then — the enlisted row committed atomically with the transaction.
        (await _CountPublishedContainingAsync(marker))
            .Should()
            .Be(1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task should_map_each_successful_save_once_and_recover_after_known_outer_rollback(
        bool synchronous,
        bool commit
    )
    {
        await using var provider = await _BuildProviderAsync();
        var saved = new List<EventContext<object>>();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);
            await using (db.Database.EnlistCommitCoordination(transaction, scope.ServiceProvider, AbortToken))
            {
                var order = new OrderEntity { Name = "two-saves" };
                db.Orders.Add(order);
                for (var i = 0; i < 2; i++)
                {
                    if (i == 1)
                    {
                        db.Orders.Add(new OrderEntity { Name = "second-business-batch" });
                    }
                    order.EmitIntegrationEvent(new OrderShipped($"evt-two-saves-{i}"));
                    saved.Add(order.GetIntegrationEvents().Single());
                    await _SaveAsync(db, synchronous);
                    order.GetIntegrationEvents().Should().BeEmpty();
                }

                (await _ReadPublishedAsync(provider, "evt-two-saves")).Should().BeEmpty();
                (await _CountOrdersAsync()).Should().Be(0);
                if (commit)
                {
                    await transaction.CommitAsync(AbortToken);
                }
                else
                {
                    await transaction.RollbackAsync(AbortToken);
                }
            }
        }

        var rows = await _ReadPublishedAsync(provider, "evt-two-saves");
        rows.Select(row => row.Id).Should().BeEquivalentTo(commit ? saved.Select(item => item.EventId) : []);
        (await _CountOrdersAsync()).Should().Be(commit ? 2 : 0);
        if (!commit)
        {
            await using var recovery = provider.CreateAsyncScope();
            var db = recovery.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
            var fresh = new OrderEntity { Name = "two-saves" };
            fresh.EmitIntegrationEvent(new OrderShipped("evt-two-saves-recovery"));
            var replay = fresh.GetIntegrationEvents().Single();
            saved.Select(item => item.EventId).Should().NotContain(replay.EventId);
            db.Orders.Add(fresh);
            await _SaveAsync(db, synchronous);
            rows = await _ReadPublishedAsync(provider, "evt-two-saves");
            rows.Should().ContainSingle();
            _AssertOccurrence(rows.Single(), replay);
            (await _CountOrdersAsync()).Should().Be(1);
        }
        else
        {
            foreach (var occurrence in saved)
            {
                _AssertOccurrence(rows.Single(row => row.Id == occurrence.EventId), occurrence);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_reuse_captured_message_identity_after_real_outbox_write_is_rolled_back_for_retry(
        bool synchronous
    )
    {
        var fault = new OutboxFault { FailuresRemaining = 1 };
        await using var provider = await _BuildProviderAsync(
            services => _AddFaultingDispatcher(services, fault),
            options => options.ReplaceService<IExecutionStrategyFactory, RetryOnceStrategyFactory>()
        );
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        var order = new OrderEntity { Name = "retry" };
        order.EmitShipping();
        db.Orders.Add(order);

        await _SaveAsync(db, synchronous);

        var evidence = provider.GetRequiredService<EmissionEvidence>();
        evidence.LocalHandlerCalls.Should().Be(1);
        evidence.Children.Should().HaveCount(2);
        fault.Attempts.Should().HaveCount(2);
        fault.Attempts[0].Should().Equal(fault.Attempts[1]);
        var rows = await _ReadPublishedAsync(provider, "evt-derived");
        rows.Should().HaveCount(2);
        foreach (var occurrence in evidence.Children)
        {
            _AssertOccurrence(rows.Single(row => row.Id == occurrence.EventId), occurrence);
        }
        (await _CountOrdersAsync()).Should().Be(1);
        order.GetIntegrationEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task should_rollback_business_and_outbox_when_bridge_fails_after_persisting()
    {
        var fault = new OutboxFault { FailuresRemaining = 1 };
        await using var provider = await _BuildProviderAsync(services => _AddFaultingDispatcher(services, fault));
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        var order = new OrderEntity { Name = "rollback" };
        order.EmitIntegrationEvent(new OrderShipped("evt-failed-save"));
        db.Orders.Add(order);

        var act = async () => await db.SaveChangesAsync(AbortToken);
        await act.Should().ThrowAsync<TransientOutboxException>();

        fault.Attempts.Should().ContainSingle();
        (await _ReadPublishedAsync(provider, "evt-failed-save")).Should().BeEmpty();
        (await _CountOrdersAsync()).Should().Be(0);
    }

    [Fact]
    public async Task should_reject_captured_system_scope_before_outbox_effects_when_tenancy_is_required()
    {
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns("unrelated-tenant");
        await using var provider = await _BuildProviderAsync(
            services => services.AddSingleton(tenant),
            requireTenant: true
        );
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();
        var order = new OrderEntity { Name = "system" };
        order.EmitIntegrationEvent(new OrderShipped("evt-system"));
        db.Orders.Add(order);

        var act = async () => await db.SaveChangesAsync(AbortToken);
        await act.Should().ThrowAsync<MissingTenantContextException>();

        (await _ReadPublishedAsync(provider, "evt-system")).Should().BeEmpty();
        (await _CountOrdersAsync()).Should().Be(0);
    }

    [Fact]
    public async Task should_preserve_consumed_root_direct_domain_causation_and_captured_system_occurrence_across_traces()
    {
        await using var provider = await _BuildProviderAsync();
        var evidence = provider.GetRequiredService<EmissionEvidence>();
        evidence.Forwarded = EventContext.Capture<object>(new OrderShipped("evt-forwarded-root"));
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "incoming-message",
            [Headers.MessageName] = "orders.ship",
            [Headers.ContractVersion] = "2",
            [Headers.CorrelationId] = "business-root",
            [Headers.TenantId] = "tenant-source",
        };
        var payload = new ShipOrder("consume");
        var message = new Message(headers, payload);
        var medium = new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = message,
            Lane = MessageLane.Bus,
            Content = provider.GetRequiredService<ISerializer>().Serialize(message),
        };
        var method = typeof(IConsume<ShipOrder>).GetMethod(nameof(IConsume<ShipOrder>.ConsumeAsync))!;
        var descriptor = new ConsumerExecutorDescriptor
        {
            ServiceTypeInfo = typeof(ShipOrderConsumer).GetTypeInfo(),
            ImplTypeInfo = typeof(ShipOrderConsumer).GetTypeInfo(),
            MethodInfo = method,
            MessageName = "orders.ship",
            GroupName = "bridge-test",
            Lane = MessageLane.Bus,
            MessageContractVersion = "2",
            Parameters = method
                .GetParameters()
                .Select(parameter => new ParameterDescriptor
                {
                    Name = parameter.Name!,
                    ParameterType = parameter.ParameterType,
                    IsFromMessaging = parameter.ParameterType == typeof(CancellationToken),
                })
                .ToArray(),
        };

        using var trace = new Activity("incoming-trace").Start();
        await provider
            .GetRequiredService<ISubscribeInvoker>()
            .InvokeAsync(new ConsumerContext(descriptor, medium), AbortToken);

        evidence.Parent.Should().NotBeNull();
        evidence.Parent.CorrelationId.Should().Be("business-root");
        evidence.Parent.CausationId.Should().Be("incoming-message");
        evidence.Parent.TenantId.Should().Be("tenant-source");
        evidence.SaveTrace.Should().NotBe(trace.TraceId);
        var rows = await _ReadPublishedAsync(provider, "evt-derived");
        rows.Should().HaveCount(2);
        evidence.Children.Select(child => child.EventId).Should().OnlyHaveUniqueItems();
        foreach (var child in evidence.Children)
        {
            child.EventId.Should().NotBe(evidence.Parent.EventId);
            child.CorrelationId.Should().Be("business-root");
            child.CausationId.Should().Be(evidence.Parent.EventId);
            child.TenantId.Should().Be("tenant-source");
            _AssertOccurrence(rows.Single(row => row.Id == child.EventId), child);
        }
        var forwarded = (await _ReadPublishedAsync(provider, "evt-forwarded-root")).Single();
        _AssertOccurrence(forwarded, evidence.Forwarded);
        (await _CountOrdersAsync()).Should().Be(1);
    }

    #region Setup

    private async Task<ServiceProvider> _BuildProviderAsync(
        Action<IServiceCollection>? configureServices = null,
        Action<DbContextOptionsBuilder>? configureDbContext = null,
        bool requireTenant = false,
        bool includeJobs = false
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessDbContextServices().AddDomainEvents().AddIntegrationEventOutbox();

        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
            setup.Options.TenantContextRequired = requireTenant;
            setup.Bus.ForMessage<OrderShipped>(message =>
            {
                message.Contract("orders.shipped", "2");
                if (includeJobs)
                {
                    message.Consumer<DeadlineConsumer>(consumer => consumer.ConsumerIdentity("tests.bridge.deadline"));
                }
            });
            setup.Bus.ForMessage<OrderInvoiced>(message => message.Contract("orders.invoiced", "3"));
            setup.Bus.ForMessage<ShipOrder>(message =>
                message
                    .Contract("orders.ship", "2")
                    .Consumer<ShipOrderConsumer>(consumer => consumer.ConsumerIdentity("tests.bridge.ship"))
            );
            setup.UseInMemory();
            setup.UsePostgreSql(fixture.ConnectionString);
        });

        services.AddSingleton<EmissionEvidence>();
        new MessagingBuilder(services).AddTenantPropagationServices();
        services.AddScoped<IDomainEventHandler<OrderShipping>, DeriveShippingFacts>();
        services.AddDbContext<BridgeTestDbContext>(options =>
        {
            options.UseNpgsql(fixture.ConnectionString).AddHeadlessExtension();
            configureDbContext?.Invoke(options);
        });
        if (includeJobs)
        {
            _RegisterJobs(services);
        }
        configureServices?.Invoke(services);

        var provider = services.BuildServiceProvider();

        // Initialize messaging outbox tables and EF business tables in the shared database. The messaging host
        // is intentionally not started, so the relay never drains rows — outbox-row assertions stay deterministic.
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeTestDbContext>();

        // A reused container may have an older fixture model. Recreate only this test's business tables so a
        // duplicate-table exception cannot hide a missing new table or roll back the collection's cleanup batch.
        await db.Database.ExecuteSqlRawAsync(
            """
            DROP TABLE IF EXISTS "DeadlineReceipts";
            DROP TABLE IF EXISTS "Orders";
            TRUNCATE TABLE messaging."published" CASCADE;
            TRUNCATE TABLE messaging."received" CASCADE;
            """,
            AbortToken
        );
        await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(AbortToken);

        return provider;
    }

    private async Task<int> _CountPublishedContainingAsync(string marker)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM messaging."published" WHERE "Content" LIKE @marker""";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "marker";
        parameter.Value = $"%{marker}%";
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(AbortToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private async Task<List<(string Id, Message Message)>> _ReadPublishedAsync(IServiceProvider provider, string marker)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """SELECT "MessageId", "Content" FROM messaging."published" WHERE "Content" LIKE @marker""";
        command.Parameters.AddWithValue("marker", $"%{marker}%");
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        var serializer = provider.GetRequiredService<ISerializer>();
        var rows = new List<(string, Message)>();
        while (await reader.ReadAsync(AbortToken))
        {
            rows.Add((reader.GetString(0), serializer.Deserialize(reader.GetString(1))!));
        }
        return rows;
    }

    private static void _AssertOccurrence((string Id, Message Message) row, EventContext<object> occurrence)
    {
        row.Id.Should().Be(occurrence.EventId);
        var headers = row.Message.Headers;
        headers[Headers.MessageId].Should().Be(occurrence.EventId);
        headers[Headers.CorrelationId].Should().Be(occurrence.CorrelationId);
        headers.TryGetValue(Headers.CausationId, out var causation).Should().Be(occurrence.CausationId is not null);
        causation.Should().Be(occurrence.CausationId);
        headers.TryGetValue(Headers.TenantId, out var tenant).Should().Be(occurrence.TenantId is not null);
        tenant.Should().Be(occurrence.TenantId);
        headers[Headers.MessageName]
            .Should()
            .Be(occurrence.Payload is OrderShipped ? "orders.shipped" : "orders.invoiced");
        headers[Headers.ContractVersion].Should().Be(occurrence.Payload is OrderShipped ? "2" : "3");
    }

    private static Task _SaveAsync(BridgeTestDbContext db, bool synchronous)
    {
        if (!synchronous)
        {
            return db.SaveChangesAsync(AbortToken);
        }
        // ReSharper disable once MethodHasAsyncOverload
        db.SaveChanges();
        return Task.CompletedTask;
    }

    private static void _AddFaultingDispatcher(IServiceCollection services, OutboxFault fault)
    {
        services.AddScoped<IHeadlessOutboxDispatcher>(provider => new FaultingDispatcher(
            new OutboxIntegrationEventDispatcher(
                provider.GetRequiredService<IBus>(),
                provider.GetRequiredService<ICurrentCommitCoordinator>(),
                new IntegrationEventPublishInvokerCache()
            ),
            fault
        ));
    }

    private async Task<int> _CountOrdersAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM "Orders" """;

        var result = await command.ExecuteScalarAsync(AbortToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    #endregion

    #region Test model

    private sealed class OutboxFault
    {
        public int FailuresRemaining { get; set; }
        public List<string[]> Attempts { get; } = [];
    }

    private sealed class FaultingDispatcher(IHeadlessOutboxDispatcher inner, OutboxFault fault)
        : IHeadlessOutboxDispatcher
    {
        public async Task DispatchAsync(
            IReadOnlyList<EventContext<object>> integrationEvents,
            CancellationToken cancellationToken = default
        )
        {
            await inner.DispatchAsync(integrationEvents, cancellationToken);
            _FailAfterWrite(integrationEvents);
        }

        public void Dispatch(IReadOnlyList<EventContext<object>> integrationEvents)
        {
            inner.Dispatch(integrationEvents);
            _FailAfterWrite(integrationEvents);
        }

        private void _FailAfterWrite(IReadOnlyList<EventContext<object>> occurrences)
        {
            fault.Attempts.Add(occurrences.Select(occurrence => occurrence.EventId).ToArray());
            if (fault.FailuresRemaining-- > 0)
            {
                throw new TransientOutboxException();
            }
        }
    }

    private sealed class TransientOutboxException : Exception;

    private sealed class RetryOnceStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TransientOutboxException;
    }

    private sealed class RetryOnceStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new RetryOnceStrategy(dependencies);
    }

    private sealed record ShipOrder(string Name);

    private sealed record OrderShipping(OrderEntity Order);

    private sealed class EmissionEvidence
    {
        public EventContext<OrderShipping>? Parent { get; set; }
        public List<EventContext<object>> Children { get; } = [];
        public EventContext<object>? Forwarded { get; set; }
        public ActivityTraceId SaveTrace { get; set; }
        public int LocalHandlerCalls { get; set; }
    }

    private sealed class ShipOrderConsumer(BridgeTestDbContext db, EmissionEvidence evidence) : IConsume<ShipOrder>
    {
        public async ValueTask ConsumeAsync(ConsumeContext<ShipOrder> context, CancellationToken cancellationToken)
        {
            using var emission = EventEmissionScope.Begin(
                new EventEmissionContext(
                    context.CorrelationId ?? context.MessageId,
                    context.MessageId,
                    context.TenantId
                )
            );
            var order = new OrderEntity { Name = context.Message.Name };
            order.EmitShipping();
            if (evidence.Forwarded is { } forwarded)
            {
                ((IIntegrationEventEmitter)order).AddIntegrationEvent(forwarded);
            }
            db.Orders.Add(order);
            using var saveTrace = new Activity("independent-save-trace")
                .SetParentId(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom())
                .Start();
            evidence.SaveTrace = saveTrace.TraceId;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class DeriveShippingFacts(EmissionEvidence evidence) : IDomainEventHandler<OrderShipping>
    {
        public ValueTask HandleAsync(EventContext<OrderShipping> context, CancellationToken cancellationToken = default)
        {
            evidence.LocalHandlerCalls++;
            evidence.Parent = context;
            context.Payload.Order.EmitIntegrationEvent(new OrderShipped("evt-derived-shipped"));
            context.Payload.Order.EmitIntegrationEvent(new OrderInvoiced("evt-derived-invoiced"));
            evidence.Children.AddRange(
                context
                    .Payload.Order.GetIntegrationEvents()
                    .Where(occurrence => !ReferenceEquals(occurrence, evidence.Forwarded))
            );
            return ValueTask.CompletedTask;
        }
    }

    private sealed record OrderShipped(string UniqueId);

    private sealed record OrderInvoiced(string UniqueId);

    private sealed class OrderEntity : AggregateRoot, IEntity<Guid>
    {
        public Guid Id { get; private init; } = Guid.NewGuid();

        public required string Name { get; init; }

        // Domain behavior that raises events through the encapsulated (protected) aggregate mutator.
        public void EmitIntegrationEvent(object integrationEvent)
        {
            AddIntegrationEvent(integrationEvent);
        }

        public void EmitShipping() => AddDomainEvent(new OrderShipping(this));

        public override IReadOnlyList<object> GetKeys()
        {
            return [Id];
        }
    }

    private sealed class BridgeTestDbContext(
        HeadlessDbContextServices services,
        DbContextOptions<BridgeTestDbContext> options
    ) : HeadlessDbContext(services, options)
    {
        public DbSet<OrderEntity> Orders => Set<OrderEntity>();

        public DbSet<DeadlineReceipt> DeadlineReceipts => Set<DeadlineReceipt>();

        public override string DefaultSchema => "";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<OrderEntity>().Property(e => e.Id).ValueGeneratedNever();
            modelBuilder.Entity<DeadlineReceipt>().Property(e => e.Id).ValueGeneratedNever();
        }
    }

    #endregion
}
