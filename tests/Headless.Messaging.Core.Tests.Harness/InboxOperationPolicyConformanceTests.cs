// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public abstract class InboxOperationPolicyConformanceTests : TestBase
{
    protected abstract void ConfigureStorage(MessagingSetupBuilder setup);

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

    private ServiceProvider _CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(ConfigureStorage);
        return services.BuildServiceProvider();
    }

    private static async ValueTask<MediumMessage> _AdmitAsync(
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

    private static async ValueTask _LeaseAsync(IDataStorage storage, MediumMessage message)
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

    private static InboxOperationRequest _Request(Guid incarnation, StatusName status) =>
        new(Guid.NewGuid(), incarnation, status, "verify existing operation policy", _Authorization());

    private static InboxAuthorizationContext _Authorization() =>
        new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "policy-operator")], "test")));
}
