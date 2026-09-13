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

public abstract class InboxOperationPolicyConformanceTests : TestBase
{
    protected abstract void ConfigureStorage(MessagingSetupBuilder setup);

    protected virtual TimeProvider CreateHistoryClock() => TimeProvider.System;

    protected abstract Task AgeHistoryAsync(ServiceProvider provider, TimeSpan age);

    protected abstract Task ExpireGenerationAsync(ServiceProvider provider, Guid storageId);

    [Fact]
    public async Task should_bound_concurrent_history_deletions_and_preserve_references()
    {
        await using var provider = _CreateProvider(CreateHistoryClock());
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        for (var i = 0; i < 4; i++)
        {
            await storage.GetInboxOperationsApi().HoldAsync(_Request(Guid.NewGuid(), StatusName.Succeeded), AbortToken);
        }
        await AgeHistoryAsync(provider, TimeSpan.FromDays(100));
        var cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        var audits = await Task.WhenAll(
            storage.DeleteExpiredInboxAuditsAsync(cutoffs, 2, AbortToken).AsTask(),
            storage.DeleteExpiredInboxAuditsAsync(cutoffs, 2, AbortToken).AsTask()
        );
        audits.Should().OnlyContain(count => count >= 0 && count <= 2);
        audits.Sum().Should().Be(4);
        var receipts = await Task.WhenAll(
            storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 2, AbortToken).AsTask(),
            storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 2, AbortToken).AsTask()
        );
        receipts.Should().OnlyContain(count => count >= 0 && count <= 2);
        var remainder = await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 4, AbortToken);
        (receipts.Sum() + remainder).Should().Be(4);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 4, AbortToken)).Should().Be(0);
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 4, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task should_reclaim_operator_history_without_releasing_hold()
    {
        await using var provider = _CreateProvider(CreateHistoryClock());
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;
        var message = await _AdmitAsync(storage, MessageLane.Bus);
        await _LeaseAsync(storage, message);
        (await storage.ChangeReceiveStateAsync(message, StatusName.Succeeded, cancellationToken: AbortToken))
            .Should()
            .BeTrue();
        var operations = storage.GetInboxOperationsApi();
        var request = _Request(message.InboxGeneration!.IncarnationId, StatusName.Succeeded);
        (await operations.HoldAsync(request, AbortToken)).Outcome.Should().Be(InboxOperationOutcome.Applied);
        await AgeHistoryAsync(provider, TimeSpan.FromDays(31));
        var cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 10, AbortToken)).Should().Be(0);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 10, AbortToken)).Should().Be(0);
        (await operations.HoldAsync(request, AbortToken)).IsReplay.Should().BeTrue();

        options.InboxOperatorAuditRetention = TimeSpan.FromDays(20);
        cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 10, AbortToken)).Should().Be(1);
        options.InboxOperatorReceiptRetention = TimeSpan.FromDays(40);
        cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 10, AbortToken)).Should().Be(0);
        options.InboxOperatorReceiptRetention = TimeSpan.FromDays(30);
        cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 10, AbortToken)).Should().Be(1);
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 10, AbortToken)).Should().Be(0);
        options.InboxOperatorAuditRetention = TimeSpan.FromDays(90);
        var evaluated = await operations.HoldAsync(request, AbortToken);
        evaluated.IsReplay.Should().BeFalse();
        evaluated.Outcome.Should().Be(InboxOperationOutcome.StateConflict);
        var rows = await operations.QueryAsync(
            new InboxGenerationQuery { IncarnationId = request.ExpectedIncarnationId },
            _Authorization(),
            AbortToken
        );
        rows.Items.Should().ContainSingle().Which.IsHeld.Should().BeTrue();
    }

    [Fact]
    public async Task should_skip_pinned_receipt_before_applying_history_batch_limit()
    {
        await using var provider = _CreateProvider(CreateHistoryClock());
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var operations = storage.GetInboxOperationsApi();
        var pinned = _Request(Guid.NewGuid(), StatusName.Succeeded);
        await operations.HoldAsync(pinned, AbortToken);
        await AgeHistoryAsync(provider, TimeSpan.FromDays(1));
        var collectible = _Request(Guid.NewGuid(), StatusName.Succeeded);
        await operations.HoldAsync(collectible, AbortToken);
        await AgeHistoryAsync(provider, TimeSpan.FromDays(100));
        var cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 1, AbortToken)).Should().Be(1);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 1, AbortToken)).Should().Be(1);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 1, AbortToken)).Should().Be(0);
        (await operations.HoldAsync(pinned with { Reason = "conflicting reuse" }, AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.OperationConflict);
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 1, AbortToken)).Should().Be(1);
        (await operations.HoldAsync(collectible, AbortToken)).IsReplay.Should().BeFalse();
        (await operations.HoldAsync(pinned, AbortToken)).IsReplay.Should().BeTrue();
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 1, AbortToken)).Should().Be(0);
        await AgeHistoryAsync(provider, TimeSpan.FromDays(100));
        cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 10, AbortToken)).Should().Be(2);
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 10, AbortToken)).Should().Be(2);
    }

    [Fact]
    public async Task should_reclaim_cleanup_history_using_independent_retention_options()
    {
        await using var provider = _CreateProvider(CreateHistoryClock());
        var initializer = provider.GetRequiredService<IStorageInitializer>();
        await initializer.InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;
        var message = await _AdmitAsync(storage, MessageLane.Bus);
        await _LeaseAsync(storage, message);
        (await storage.ChangeReceiveStateAsync(message, StatusName.Succeeded, cancellationToken: AbortToken))
            .Should()
            .BeTrue();
        await ExpireGenerationAsync(provider, message.StorageId);
        (await storage.DeleteExpiresAsync(initializer.GetReceivedTableName(), DateTimeOffset.MaxValue, 10, AbortToken))
            .Should()
            .Be(1);
        await AgeHistoryAsync(provider, TimeSpan.FromDays(8));
        options.InboxCleanupAuditRetention = TimeSpan.FromDays(10);
        var cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 10, AbortToken)).Should().Be(0);
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 10, AbortToken)).Should().Be(0);
        options.InboxCleanupAuditRetention = TimeSpan.FromDays(7);
        options.InboxCleanupReceiptRetention = TimeSpan.FromDays(10);
        cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 10, AbortToken)).Should().Be(1);
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 10, AbortToken)).Should().Be(0);
        options.InboxCleanupReceiptRetention = TimeSpan.FromDays(7);
        cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 10, AbortToken)).Should().Be(1);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 10, AbortToken)).Should().Be(0);
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 10, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task should_cancel_history_storage_operations()
    {
        await using var provider = _CreateProvider(CreateHistoryClock());
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var snapshot = async () => await storage.GetInboxHistoryRetentionCutoffsAsync(cancelled.Token);
        var audits = async () => await storage.DeleteExpiredInboxAuditsAsync(default, 1, cancelled.Token);
        var receipts = async () => await storage.DeleteExpiredInboxReceiptsAsync(default, 1, cancelled.Token);
        await snapshot.Should().ThrowAsync<OperationCanceledException>();
        await audits.Should().ThrowAsync<OperationCanceledException>();
        await receipts.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(MessageLane.Bus, false)]
    [InlineData(MessageLane.Bus, true)]
    [InlineData(MessageLane.Queue, false)]
    [InlineData(MessageLane.Queue, true)]
    public async Task should_preserve_operation_outcomes_through_inbox_lifecycle(MessageLane lane, bool orphaned)
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var operations = storage.GetInboxOperationsApi();
        var message = await _AdmitAsync(storage, lane);
        var incarnation = message.InboxGeneration!.IncarnationId;

        (await operations.HoldAsync(_Request(Guid.NewGuid(), StatusName.Failed), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.NotFound);
        (await operations.HoldAsync(_Request(incarnation, StatusName.Failed), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.StateConflict);
        await _LeaseAsync(storage, message);
        if (orphaned)
        {
            (await storage.MarkReceivedInboxOrphanedAsync(message, true, AbortToken)).Should().BeTrue();
        }
        (await operations.HoldAsync(_Request(incarnation, StatusName.Scheduled), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Active);

        (
            await storage.ChangeReceiveStateAsync(
                message,
                StatusName.Failed,
                nextRetryAt: message.LockedUntil!.Value.AddHours(1),
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();
        (await operations.ForceReprocessAsync(_Request(incarnation, StatusName.Failed), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Active, "scheduled retries remain active even when orphaned");

        await _LeaseAsync(storage, message);
        (await storage.ChangeReceiveStateAsync(message, StatusName.Failed, cancellationToken: AbortToken))
            .Should()
            .BeTrue();
        var terminal = await operations.QueryAsync(
            new InboxGenerationQuery { IncarnationId = incarnation },
            _Authorization(),
            AbortToken
        );
        terminal.Items.Should().ContainSingle().Which.IsOrphaned.Should().Be(orphaned);
        (await operations.HoldAsync(_Request(incarnation, StatusName.Succeeded), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.StateConflict);
        (await operations.HoldAsync(_Request(incarnation, StatusName.Failed), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);
        (await operations.HoldAsync(_Request(incarnation, StatusName.Failed), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.StateConflict);
        (await operations.PurgeAsync(_Request(incarnation, StatusName.Failed), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Held);

        var replay = await operations.ForceReprocessAsync(_Request(incarnation, StatusName.Failed), AbortToken);
        replay.Outcome.Should().Be(InboxOperationOutcome.Applied, "a hold currently blocks purge, not force-reprocess");
        replay.ChildGeneration.Should().Be(1);
        (await operations.ForceReprocessAsync(_Request(incarnation, StatusName.Failed), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.StateConflict);
        (await operations.ReleaseHoldAsync(_Request(incarnation, StatusName.Failed), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);
        (await operations.ReleaseHoldAsync(_Request(incarnation, StatusName.Failed), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.StateConflict);
        (await operations.PurgeAsync(_Request(incarnation, StatusName.Failed), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);
        (await operations.HoldAsync(_Request(incarnation, StatusName.Failed), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.NotFound);
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_reject_generation_overflow_without_blocking_hold(MessageLane lane)
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var message = await _AdmitAsync(storage, lane, long.MaxValue);
        await _LeaseAsync(storage, message);
        (await storage.ChangeReceiveStateAsync(message, StatusName.Succeeded, cancellationToken: AbortToken))
            .Should()
            .BeTrue();
        var operations = storage.GetInboxOperationsApi();
        var incarnation = message.InboxGeneration!.IncarnationId;
        (await operations.ForceReprocessAsync(_Request(incarnation, StatusName.Succeeded), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.StateConflict);
        (await operations.HoldAsync(_Request(incarnation, StatusName.Succeeded), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_hold_unclaimed_scheduled_orphan(MessageLane lane)
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var message = await _AdmitAsync(storage, lane);
        await _LeaseAsync(storage, message);
        (await storage.DeferReceivedInboxOrphanAsync(message, AbortToken)).Should().BeTrue();

        var result = await storage
            .GetInboxOperationsApi()
            .HoldAsync(_Request(message.InboxGeneration!.IncarnationId, StatusName.Scheduled), AbortToken);

        result.Outcome.Should().Be(InboxOperationOutcome.Applied);
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_release_and_purge_unclaimed_orphan_with_scheduled_retry(MessageLane lane)
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var message = await _AdmitAsync(storage, lane);
        await _LeaseAsync(storage, message);
        (await storage.DeferReceivedInboxOrphanAsync(message, AbortToken)).Should().BeTrue();
        var operations = storage.GetInboxOperationsApi();
        var incarnation = message.InboxGeneration!.IncarnationId;

        (await operations.HoldAsync(_Request(Guid.NewGuid(), StatusName.Scheduled), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.NotFound);
        (await operations.HoldAsync(_Request(incarnation, StatusName.Failed), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.StateConflict);
        (await operations.ForceReprocessAsync(_Request(incarnation, StatusName.Scheduled), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Active);
        (await operations.HoldAsync(_Request(incarnation, StatusName.Scheduled), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);
        (await operations.PurgeAsync(_Request(incarnation, StatusName.Scheduled), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Held);
        (await operations.ReleaseHoldAsync(_Request(incarnation, StatusName.Scheduled), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);
        (await operations.PurgeAsync(_Request(incarnation, StatusName.Scheduled), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);
        (
            await storage.LeaseReceiveAndReserveAttemptAsync(
                message,
                TimeSpan.FromMinutes(5),
                message.InlineAttempts,
                AbortToken
            )
        )
            .Should()
            .BeFalse();
        (await storage.ConfirmReceivedInboxRoutableAsync(message, AbortToken)).Should().BeFalse();
    }

    [Theory]
    [InlineData(MessageLane.Bus, false)]
    [InlineData(MessageLane.Bus, true)]
    [InlineData(MessageLane.Queue, false)]
    [InlineData(MessageLane.Queue, true)]
    public async Task should_block_orphan_operations_during_live_claim(MessageLane lane, bool held)
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var message = await _AdmitAsync(storage, lane);
        await _LeaseAsync(storage, message);
        (await storage.DeferReceivedInboxOrphanAsync(message, AbortToken)).Should().BeTrue();
        var operations = storage.GetInboxOperationsApi();
        var incarnation = message.InboxGeneration!.IncarnationId;
        if (held)
        {
            (await operations.HoldAsync(_Request(incarnation, StatusName.Scheduled), AbortToken))
                .Outcome.Should()
                .Be(InboxOperationOutcome.Applied);
        }
        await _LeaseAsync(storage, message);

        (await operations.HoldAsync(_Request(incarnation, StatusName.Scheduled), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Active);
        (await operations.ReleaseHoldAsync(_Request(incarnation, StatusName.Scheduled), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Active);
        (await operations.PurgeAsync(_Request(incarnation, StatusName.Scheduled), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Active);
        (await storage.ConfirmReceivedInboxRoutableAsync(message, AbortToken)).Should().BeTrue();
        (await storage.ChangeReceiveStateAsync(message, StatusName.Succeeded, cancellationToken: AbortToken))
            .Should()
            .BeTrue();
        var result = await operations.QueryAsync(
            new InboxGenerationQuery { IncarnationId = incarnation },
            _Authorization(),
            AbortToken
        );
        result.Items.Should().ContainSingle().Which.IsHeld.Should().Be(held);
        result.Items.Single().IsOrphaned.Should().BeFalse();
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_allow_exactly_one_recovery_claim_or_purge(MessageLane lane)
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var message = await _AdmitAsync(storage, lane);
            await _LeaseAsync(storage, message);
            (await storage.DeferReceivedInboxOrphanAsync(message, AbortToken)).Should().BeTrue();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var claim = Task.Run(
                async () =>
                {
                    await start.Task;
                    return await storage.LeaseReceiveAndReserveAttemptAsync(
                        message,
                        TimeSpan.FromMinutes(5),
                        message.InlineAttempts,
                        AbortToken
                    );
                },
                AbortToken
            );
            var purge = Task.Run(
                async () =>
                {
                    await start.Task;
                    return await storage
                        .GetInboxOperationsApi()
                        .PurgeAsync(_Request(message.InboxGeneration!.IncarnationId, StatusName.Scheduled), AbortToken);
                },
                AbortToken
            );
            start.SetResult();
            await _AssertRecoveryPurgeAsync(storage, message, claim, purge);
        }
    }

    protected static async Task _AssertRecoveryPurgeAsync(
        IDataStorage storage,
        MediumMessage message,
        Task<bool> claim,
        Task<InboxOperationResult> purge
    )
    {
        var claimed = await claim.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
        var purged = await purge.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
        purged.Outcome.Should().Be(claimed ? InboxOperationOutcome.Active : InboxOperationOutcome.Applied);
        (await storage.ConfirmReceivedInboxRoutableAsync(message, AbortToken)).Should().Be(claimed);
        var rows = await storage
            .GetInboxOperationsApi()
            .QueryAsync(
                new InboxGenerationQuery { IncarnationId = message.InboxGeneration!.IncarnationId },
                _Authorization(),
                AbortToken
            );
        rows.Items.Should().HaveCount(claimed ? 1 : 0);
    }

    [Fact]
    public async Task should_preserve_receipt_creation_time_and_replay_after_generation_purge()
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var message = await _AdmitAsync(storage, MessageLane.Bus);
        await _LeaseAsync(storage, message);
        (await storage.ChangeReceiveStateAsync(message, StatusName.Succeeded, cancellationToken: AbortToken))
            .Should()
            .BeTrue();
        var operations = storage.GetInboxOperationsApi();
        var request = _Request(message.InboxGeneration!.IncarnationId, StatusName.Succeeded);
        var original = await operations.HoldAsync(request, AbortToken);
        original.Outcome.Should().Be(InboxOperationOutcome.Applied);
        (await operations.ReleaseHoldAsync(_Request(request.ExpectedIncarnationId, StatusName.Succeeded), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);
        (await operations.PurgeAsync(_Request(request.ExpectedIncarnationId, StatusName.Succeeded), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);

        var replay = await operations.HoldAsync(request, AbortToken);
        replay.Should().Be(original with { IsReplay = true });
        replay.CreatedAt.Should().Be(original.CreatedAt);
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

    protected static async ValueTask<MediumMessage> _AdmitAsync(
        IDataStorage storage,
        MessageLane lane,
        long generation = 0
    )
    {
        var origin = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = Guid.NewGuid().ToString(),
                [Headers.MessageName] = "operations.contract",
                [Headers.Group] = "operations.group",
            },
            "payload"
        );
        var admission = await storage.AdmitReceivedMessageAsync(
            "operations.contract",
            "operations.group",
            "operations.consumer",
            "v1",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Content = string.Empty,
                Lane = lane,
                Origin = origin,
            },
            generation,
            cancellationToken: AbortToken
        );
        return admission.Message;
    }

    protected static async ValueTask _LeaseAsync(IDataStorage storage, MediumMessage message)
    {
        var originalAttempts = message.InlineAttempts++;
        (
            await storage.LeaseReceiveAndReserveAttemptAsync(
                message,
                TimeSpan.FromMinutes(5),
                originalAttempts,
                AbortToken
            )
        )
            .Should()
            .BeTrue();
    }

    protected static InboxOperationRequest _Request(Guid incarnation, StatusName status) =>
        new(Guid.NewGuid(), incarnation, status, "verify existing operation policy", _Authorization());

    protected static InboxAuthorizationContext _Authorization() =>
        new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "policy-operator")], "test")));
}
