// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class InMemoryInboxOperationPolicyTests : InboxOperationPolicyConformanceTests
{
    protected override TimeProvider CreateHistoryClock() => new FakeTimeProvider(DateTimeOffset.UtcNow);

    [Fact]
    public async Task should_expire_history_at_exact_retention_boundary()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var provider = _CreateProvider(clock);
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        provider.GetRequiredService<IOptions<MessagingOptions>>().Value.InboxOperatorAuditRetention = TimeSpan.FromDays(
            30
        );
        await storage.GetInboxOperationsApi().HoldAsync(_Request(Guid.NewGuid(), StatusName.Succeeded), AbortToken);
        clock.Advance(TimeSpan.FromDays(30).Subtract(TimeSpan.FromTicks(1)));
        var cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 1, AbortToken)).Should().Be(0);
        clock.Advance(TimeSpan.FromTicks(1));
        cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 1, AbortToken)).Should().Be(1);
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 1, AbortToken)).Should().Be(1);
    }

    protected override Task AgeHistoryAsync(ServiceProvider provider, TimeSpan age)
    {
        ((FakeTimeProvider)provider.GetRequiredService<TimeProvider>()).Advance(age);
        return Task.CompletedTask;
    }

    protected override Task ExpireGenerationAsync(ServiceProvider provider, Guid storageId) =>
        AgeHistoryAsync(provider, TimeSpan.FromDays(100));

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_use_store_clock_and_recover_held_orphan(MessageLane lane)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var provider = _CreateProvider(clock);
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var message = await _AdmitAsync(storage, lane);
        await _LeaseAsync(storage, message);
        (await storage.MarkReceivedInboxOrphanedAsync(message, true, AbortToken)).Should().BeTrue();
        var operations = storage.GetInboxOperationsApi();
        var incarnation = message.InboxGeneration!.IncarnationId;
        (await operations.HoldAsync(_Request(incarnation, StatusName.Scheduled), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Active);
        clock.Advance(TimeSpan.FromDays(1));
        (await operations.HoldAsync(_Request(incarnation, StatusName.Scheduled), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);
        var recovered = (await storage.GetReceivedInboxOrphansOfNeedRetryAsync(lane, AbortToken))
            .Should()
            .ContainSingle()
            .Which;
        (await storage.ConfirmReceivedInboxRoutableAsync(recovered, AbortToken)).Should().BeTrue();
        (await storage.ChangeReceiveStateAsync(recovered, StatusName.Succeeded, cancellationToken: AbortToken))
            .Should()
            .BeTrue();
        var rows = await operations.QueryAsync(
            new InboxGenerationQuery { IncarnationId = incarnation },
            _Authorization(),
            AbortToken
        );
        rows.Items.Should().ContainSingle().Which.IsHeld.Should().BeTrue();
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_not_claim_removed_row_after_waiting_for_serialization(MessageLane lane)
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = (InMemoryDataStorage)provider.GetRequiredService<IDataStorage>();
        var message = await _AdmitAsync(storage, lane);
        await _LeaseAsync(storage, message);
        (await storage.DeferReceivedInboxOrphanAsync(message, AbortToken)).Should().BeTrue();
        var row = storage.ReceivedMessages[message.StorageId];
        var collectionLock = (Lock)
            typeof(InMemoryDataStorage)
                .GetField(
                    "_receivedUpsertLock",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.DeclaredOnly
                )!
                .GetValue(storage)!;
        ValueTask<bool> claim = default;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var claimant = new Thread(() =>
        {
            try
            {
                claim = storage.LeaseReceiveAndReserveAttemptAsync(
                    message,
                    TimeSpan.FromMinutes(5),
                    message.InlineAttempts,
                    AbortToken
                );
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        ValueTask<InboxOperationResult> purge;
        // Holding both locks makes the old row-only claimant and the corrected collection-first
        // claimant stop before mutation. Purge must invalidate the waiting claim in either case.
        lock (collectionLock)
        {
            lock (row)
            {
                claimant.Start();
                SpinWait
                    .SpinUntil(() => claimant.ThreadState.HasFlag(ThreadState.WaitSleepJoin), TimeSpan.FromSeconds(5))
                    .Should()
                    .BeTrue();
                purge = storage
                    .GetInboxOperationsApi()
                    .PurgeAsync(_Request(message.InboxGeneration!.IncarnationId, StatusName.Scheduled), AbortToken);
                purge.IsCompletedSuccessfully.Should().BeTrue();
            }
        }
        (await purge).Outcome.Should().Be(InboxOperationOutcome.Applied);
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        (await claim).Should().BeFalse();
        storage.ReceivedMessages.Should().NotContainKey(message.StorageId);
        (await storage.ConfirmReceivedInboxRoutableAsync(message, AbortToken)).Should().BeFalse();
    }

    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
        setup.UseInMemoryStorage();
    }
}
