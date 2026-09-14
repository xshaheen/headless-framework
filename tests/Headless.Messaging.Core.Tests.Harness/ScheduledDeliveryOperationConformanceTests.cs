// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public abstract class ScheduledDeliveryOperationConformanceTests : TestBase
{
    protected abstract void ConfigureStorage(MessagingSetupBuilder setup);

    protected virtual TimeProvider CreateHistoryClock() => TimeProvider.System;

    protected abstract Task AgeHistoryAsync(ServiceProvider provider, TimeSpan age);

    [Fact]
    public async Task should_list_only_pending_scheduled_deliveries_and_filter_by_storage_ids()
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var operations = storage.GetScheduledDeliveryOperationsApi();
        var clock = provider.GetRequiredService<TimeProvider>();

        // 1. Pending Delayed row (unleased)
        var due1 = clock.GetUtcNow().AddHours(2);
        var delayedRow = await _StoreScheduledAsync(storage, "orders.delayed", due1, MessageLane.Bus);

        // 2. Pending Queued row (leased)
        var due2 = clock.GetUtcNow().AddHours(3);
        var queuedRow = await _StoreScheduledAsync(storage, "orders.queued", due2, MessageLane.Queue);
        await storage.ChangePublishStateAsync(queuedRow, StatusName.Queued, cancellationToken: AbortToken);
        await storage.LeasePublishAsync(queuedRow, TimeSpan.FromMinutes(10), AbortToken);

        // 3. Succeeded row (not pending)
        var due3 = clock.GetUtcNow().AddHours(1);
        var succeededRow = await _StoreScheduledAsync(storage, "orders.succeeded", due3, MessageLane.Bus);
        await storage.ChangePublishStateAsync(succeededRow, StatusName.Succeeded, cancellationToken: AbortToken);

        // Query all
        var query = new ScheduledDeliveryQuery();
        var page = await operations.QueryAsync(query, _Authorization(), AbortToken);

        // Only delayedRow and queuedRow appear; both are "Pending"
        page.Items.Should().Contain(x => x.StorageId == delayedRow.StorageId);
        page.Items.Should().Contain(x => x.StorageId == queuedRow.StorageId);
        page.Items.Should().NotContain(x => x.StorageId == succeededRow.StorageId);

        var delayedView = page.Items.Single(x => x.StorageId == delayedRow.StorageId);
        delayedView.Status.Should().Be("Pending");
        delayedView.IsLeased.Should().BeFalse();
        delayedView.Lane.Should().Be(MessageLane.Bus);
        delayedView.MessageName.Should().Be("orders.delayed");

        var queuedView = page.Items.Single(x => x.StorageId == queuedRow.StorageId);
        queuedView.Status.Should().Be("Pending");
        queuedView.IsLeased.Should().BeTrue();
        queuedView.Lane.Should().Be(MessageLane.Queue);
        queuedView.MessageName.Should().Be("orders.queued");

        // Filter by StorageIds
        var filteredQuery = new ScheduledDeliveryQuery { StorageIds = [delayedRow.StorageId] };
        var filteredPage = await operations.QueryAsync(filteredQuery, _Authorization(), AbortToken);
        filteredPage.Items.Should().ContainSingle(x => x.StorageId == delayedRow.StorageId);
    }

    [Fact]
    public async Task should_apply_revoke_delete_row_and_record_receipt_and_audit()
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var operations = storage.GetScheduledDeliveryOperationsApi();
        var clock = provider.GetRequiredService<TimeProvider>();

        var due = clock.GetUtcNow().AddHours(2);
        var stored = await _StoreScheduledAsync(storage, "orders.revoke", due, MessageLane.Bus);

        // First list it to get authoritative expected due at
        var list = await operations.QueryAsync(
            new ScheduledDeliveryQuery { StorageIds = [stored.StorageId] },
            _Authorization(),
            AbortToken
        );
        var view = list.Items.Single();

        var operationId = Guid.NewGuid();
        var request = new ScheduledDeliveryOperationRequest(
            operationId,
            stored.StorageId,
            view.ExpectedDueAt,
            "operator revoke test",
            _Authorization("operator-alice")
        );

        var result = await operations.RevokeAsync(request, AbortToken);
        result.Outcome.Should().Be(InboxOperationOutcome.Applied);
        result.IsReplay.Should().BeFalse();
        result.Actor.Should().Be("operator-alice");
        result.StorageId.Should().Be(stored.StorageId);
        result.MessageName.Should().Be("orders.revoke");
        result.Lane.Should().Be(MessageLane.Bus);

        // Row is deleted from listing
        var afterList = await operations.QueryAsync(
            new ScheduledDeliveryQuery { StorageIds = [stored.StorageId] },
            _Authorization(),
            AbortToken
        );
        afterList.Items.Should().BeEmpty();

        // Replay of identical request returns same result flagged as replay
        var replay = await operations.RevokeAsync(request, AbortToken);
        replay.Outcome.Should().Be(InboxOperationOutcome.Applied);
        replay.IsReplay.Should().BeTrue();
        replay.OperationId.Should().Be(operationId);
        replay.Actor.Should().Be("operator-alice");
        replay.CreatedAt.Should().Be(result.CreatedAt);
    }

    [Fact]
    public async Task should_return_active_when_revoking_row_with_reserved_attempt()
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var operations = storage.GetScheduledDeliveryOperationsApi();
        var clock = provider.GetRequiredService<TimeProvider>();

        var due = clock.GetUtcNow().AddHours(2);
        var stored = await _StoreScheduledAsync(storage, "orders.active_attempt", due, MessageLane.Bus);

        // Reserve attempt
        stored.InlineAttempts = 1;
        (await storage.LeasePublishAndReserveAttemptAsync(stored, TimeSpan.FromMinutes(1), 0, AbortToken))
            .Should()
            .BeTrue();

        var request = new ScheduledDeliveryOperationRequest(
            Guid.NewGuid(),
            stored.StorageId,
            due,
            "revoke active attempt",
            _Authorization()
        );

        var result = await operations.RevokeAsync(request, AbortToken);
        result.Outcome.Should().Be(InboxOperationOutcome.Active);
    }

    [Fact]
    public async Task should_apply_dispatch_now_on_unleased_delayed_row()
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var operations = storage.GetScheduledDeliveryOperationsApi();
        var clock = provider.GetRequiredService<TimeProvider>();

        var due = clock.GetUtcNow().AddHours(5);
        var stored = await _StoreScheduledAsync(storage, "orders.dispatch_now", due, MessageLane.Bus);

        var list = await operations.QueryAsync(
            new ScheduledDeliveryQuery { StorageIds = [stored.StorageId] },
            _Authorization(),
            AbortToken
        );
        var view = list.Items.Single();

        var request = new ScheduledDeliveryOperationRequest(
            Guid.NewGuid(),
            stored.StorageId,
            view.ExpectedDueAt,
            "dispatch now test",
            _Authorization("operator-bob")
        );

        var result = await operations.DispatchNowAsync(request, AbortToken);
        result.Outcome.Should().Be(InboxOperationOutcome.Applied);
        result.IsReplay.Should().BeFalse();
        result.Actor.Should().Be("operator-bob");

        // The row's due instant should now be at or before provider clock
        var afterList = await operations.QueryAsync(
            new ScheduledDeliveryQuery { StorageIds = [stored.StorageId] },
            _Authorization(),
            AbortToken
        );
        afterList.Items.Should().ContainSingle();
        var updatedView = afterList.Items.Single();
        updatedView.ExpectedDueAt.Should().BeOnOrBefore(clock.GetUtcNow().AddSeconds(10));
        updatedView.IsLeased.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_active_when_dispatching_now_on_leased_row()
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var operations = storage.GetScheduledDeliveryOperationsApi();
        var clock = provider.GetRequiredService<TimeProvider>();

        var due = clock.GetUtcNow().AddHours(5);
        var stored = await _StoreScheduledAsync(storage, "orders.leased_dispatch", due, MessageLane.Bus);

        // Lease the row
        (await storage.LeasePublishAsync(stored, TimeSpan.FromMinutes(10), AbortToken))
            .Should()
            .BeTrue();

        var list = await operations.QueryAsync(
            new ScheduledDeliveryQuery { StorageIds = [stored.StorageId] },
            _Authorization(),
            AbortToken
        );
        var view = list.Items.Single();
        view.IsLeased.Should().BeTrue();

        var request = new ScheduledDeliveryOperationRequest(
            Guid.NewGuid(),
            stored.StorageId,
            view.ExpectedDueAt,
            "dispatch leased row",
            _Authorization()
        );

        var result = await operations.DispatchNowAsync(request, AbortToken);
        result.Outcome.Should().Be(InboxOperationOutcome.Active);
    }

    [Fact]
    public async Task should_record_operation_conflict_when_same_operation_id_has_different_request()
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var operations = storage.GetScheduledDeliveryOperationsApi();
        var clock = provider.GetRequiredService<TimeProvider>();

        var due = clock.GetUtcNow().AddHours(2);
        var stored = await _StoreScheduledAsync(storage, "orders.conflict", due, MessageLane.Bus);

        var list = await operations.QueryAsync(
            new ScheduledDeliveryQuery { StorageIds = [stored.StorageId] },
            _Authorization(),
            AbortToken
        );
        var view = list.Items.Single();

        var operationId = Guid.NewGuid();
        var request1 = new ScheduledDeliveryOperationRequest(
            operationId,
            stored.StorageId,
            view.ExpectedDueAt,
            "initial reason",
            _Authorization("operator-alice")
        );
        var result1 = await operations.RevokeAsync(request1, AbortToken);
        result1.Outcome.Should().Be(InboxOperationOutcome.Applied);

        // Same operation ID with different reason
        var request2 = new ScheduledDeliveryOperationRequest(
            operationId,
            stored.StorageId,
            view.ExpectedDueAt,
            "conflicting reason",
            _Authorization("operator-alice")
        );
        var result2 = await operations.RevokeAsync(request2, AbortToken);
        result2.Outcome.Should().Be(InboxOperationOutcome.OperationConflict);
        result2.IsReplay.Should().BeTrue();
    }

    [Fact]
    public async Task should_record_state_conflict_on_due_instant_mismatch()
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var operations = storage.GetScheduledDeliveryOperationsApi();
        var clock = provider.GetRequiredService<TimeProvider>();

        var due = clock.GetUtcNow().AddHours(2);
        var stored = await _StoreScheduledAsync(storage, "orders.mismatch", due, MessageLane.Bus);

        var mismatchedDue = due.AddMinutes(15);
        var request = new ScheduledDeliveryOperationRequest(
            Guid.NewGuid(),
            stored.StorageId,
            mismatchedDue,
            "mismatch test",
            _Authorization()
        );

        var result = await operations.RevokeAsync(request, AbortToken);
        result.Outcome.Should().Be(InboxOperationOutcome.StateConflict);

        // Row is still there
        var list = await operations.QueryAsync(
            new ScheduledDeliveryQuery { StorageIds = [stored.StorageId] },
            _Authorization(),
            AbortToken
        );
        list.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task should_round_trip_sub_millisecond_due_instant_through_listing_to_fenced_action()
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var operations = storage.GetScheduledDeliveryOperationsApi();
        var clock = provider.GetRequiredService<TimeProvider>();

        // High-precision sub-millisecond instant
        var ticks = 1234567L; // non-zero sub-millisecond ticks
        var due = clock.GetUtcNow().AddHours(2) + TimeSpan.FromTicks(ticks);
        var stored = await _StoreScheduledAsync(storage, "orders.precision", due, MessageLane.Bus);

        var list = await operations.QueryAsync(
            new ScheduledDeliveryQuery { StorageIds = [stored.StorageId] },
            _Authorization(),
            AbortToken
        );
        var view = list.Items.Single();

        // Exact round-trip check
        var request = new ScheduledDeliveryOperationRequest(
            Guid.NewGuid(),
            stored.StorageId,
            view.ExpectedDueAt,
            "precision test",
            _Authorization()
        );

        var result = await operations.RevokeAsync(request, AbortToken);
        result.Outcome.Should().Be(InboxOperationOutcome.Applied);
    }

    [Fact]
    public async Task should_outlive_row_and_delete_scheduled_audits_before_receipts_at_retention_cutoffs()
    {
        await using var provider = _CreateProvider(CreateHistoryClock());
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var operations = storage.GetScheduledDeliveryOperationsApi();
        var clock = provider.GetRequiredService<TimeProvider>();

        provider.GetRequiredService<IOptions<MessagingOptions>>().Value.InboxOperatorAuditRetention = TimeSpan.FromDays(
            30
        );
        provider.GetRequiredService<IOptions<MessagingOptions>>().Value.InboxOperatorReceiptRetention =
            TimeSpan.FromDays(30);

        var due = clock.GetUtcNow().AddHours(2);
        var stored = await _StoreScheduledAsync(storage, "orders.retention", due, MessageLane.Bus);
        var list = await operations.QueryAsync(
            new ScheduledDeliveryQuery { StorageIds = [stored.StorageId] },
            _Authorization(),
            AbortToken
        );
        var view = list.Items.Single();

        var operationId = Guid.NewGuid();
        var result = await operations.RevokeAsync(
            new ScheduledDeliveryOperationRequest(
                operationId,
                stored.StorageId,
                view.ExpectedDueAt,
                "retention test",
                _Authorization()
            ),
            AbortToken
        );
        result.Outcome.Should().Be(InboxOperationOutcome.Applied);

        // Also create a young inbox audit
        var inboxIncarnation = Guid.NewGuid();
        await storage
            .GetInboxOperationsApi()
            .HoldAsync(
                new InboxOperationRequest(
                    Guid.NewGuid(),
                    inboxIncarnation,
                    StatusName.Succeeded,
                    "young inbox",
                    _Authorization()
                ),
                AbortToken
            );

        // Age past operator retention cutoffs
        await AgeHistoryAsync(provider, TimeSpan.FromDays(40));

        var cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);

        // Scheduled audit should be deleted
        var auditsDeleted = await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 10, AbortToken);
        auditsDeleted.Should().BeGreaterThanOrEqualTo(1);

        // Scheduled receipt should now be eligible for deletion (audit is gone)
        var receiptsDeleted = await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 10, AbortToken);
        receiptsDeleted.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task should_allow_exactly_one_action_when_concurrent_revoke_and_dispatch_now_race()
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var operations = storage.GetScheduledDeliveryOperationsApi();
        var clock = provider.GetRequiredService<TimeProvider>();

        var due = clock.GetUtcNow().AddHours(3);
        var stored = await _StoreScheduledAsync(storage, "orders.race", due, MessageLane.Bus);

        var list = await operations.QueryAsync(
            new ScheduledDeliveryQuery { StorageIds = [stored.StorageId] },
            _Authorization(),
            AbortToken
        );
        var view = list.Items.Single();

        var revokeRequest = new ScheduledDeliveryOperationRequest(
            Guid.NewGuid(),
            stored.StorageId,
            view.ExpectedDueAt,
            "race revoke",
            _Authorization()
        );
        var dispatchRequest = new ScheduledDeliveryOperationRequest(
            Guid.NewGuid(),
            stored.StorageId,
            view.ExpectedDueAt,
            "race dispatch",
            _Authorization()
        );

        var revokeTask = operations.RevokeAsync(revokeRequest, AbortToken).AsTask();
        var dispatchTask = operations.DispatchNowAsync(dispatchRequest, AbortToken).AsTask();

        var results = await Task.WhenAll(revokeTask, dispatchTask);
        var appliedCount = results.Count(r => r.Outcome == InboxOperationOutcome.Applied);
        appliedCount.Should().Be(1);

        var other = results.Single(r => r.Outcome != InboxOperationOutcome.Applied);
        other.Outcome.Should().BeOneOf(InboxOperationOutcome.NotFound, InboxOperationOutcome.StateConflict);
    }

    protected ServiceProvider _CreateProvider(TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (timeProvider is not null)
        {
            services.AddSingleton(timeProvider);
        }
        services.AddHeadlessMessaging(ConfigureStorage);
        return services.BuildServiceProvider();
    }

    protected static async ValueTask<MediumMessage> _StoreScheduledAsync(
        IDataStorage storage,
        string messageName,
        DateTimeOffset publishAt,
        MessageLane lane
    )
    {
        var origin = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = Guid.NewGuid().ToString(),
                [Headers.MessageName] = messageName,
            },
            "payload"
        );
        return await storage.StoreScheduledMessageAsync(
            messageName,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Content = string.Empty,
                Lane = lane,
                Origin = origin,
            },
            publishAt,
            cancellationToken: default
        );
    }

    protected static OperatorAuthorizationContext _Authorization(string actor = "scheduled-operator") =>
        new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, actor)], "test")));
}
