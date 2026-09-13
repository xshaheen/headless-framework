// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Shared inbox admission and lease fence checks for every storage provider.</summary>
public abstract class InboxStorageConformanceTests : TestBase
{
    protected abstract void ConfigureStorage(MessagingSetupBuilder setup);

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_defer_orphan_and_release_ownership_without_consuming_failure_retries(MessageLane lane)
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var envelope = _CreateMessage(lane);
        var winner = (await _AdmitAsync(storage, envelope)).Message;
        var previousAttempts = winner.InlineAttempts++;
        (
            await storage.LeaseReceiveAndReserveAttemptAsync(
                winner,
                TimeSpan.FromMinutes(5),
                previousAttempts,
                AbortToken
            )
        )
            .Should()
            .BeTrue();
        var retries = winner.Retries;
        var before = DateTimeOffset.UtcNow;

        (await storage.DeferReceivedInboxOrphanAsync(winner, AbortToken)).Should().BeTrue();

        var persisted = (await _AdmitAsync(storage, envelope)).Message;
        persisted.IsInboxOrphaned.Should().BeTrue();
        persisted.Owner.Should().BeNull();
        persisted.LockedUntil.Should().BeNull();
        persisted.NextRetryAt.Should().BeAfter(before);
        persisted.Retries.Should().Be(retries);
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_require_every_fence_component_for_orphan_deferral_and_confirmation(MessageLane lane)
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var envelope = _CreateMessage(lane);
        var message = (await _AdmitAsync(storage, envelope)).Message;
        message.InlineAttempts++;
        (await storage.LeaseReceiveAndReserveAttemptAsync(message, TimeSpan.FromMinutes(5), 0, AbortToken))
            .Should()
            .BeTrue();
        var fence = message.InboxAttemptFence!;
        foreach (var invalidFence in _InvalidFences(fence).Values)
        {
            message.InboxAttemptFence = invalidFence;
            (await storage.DeferReceivedInboxOrphanAsync(message, AbortToken)).Should().BeFalse();
            (await storage.ConfirmReceivedInboxRoutableAsync(message, AbortToken)).Should().BeFalse();
            var unchanged = (await _AdmitAsync(storage, envelope)).Message;
            unchanged.IsInboxOrphaned.Should().BeFalse();
            unchanged.InboxAttemptFence.Should().Be(fence);
            unchanged.NextRetryAt.Should().Be(message.NextRetryAt);
        }
        message.InboxAttemptFence = fence;
        (await storage.ConfirmReceivedInboxRoutableAsync(message, AbortToken))
            .Should()
            .BeTrue("already-routable is accepted under the exact fence");
        (await storage.DeferReceivedInboxOrphanAsync(message, AbortToken)).Should().BeTrue();
        (await storage.ConfirmReceivedInboxRoutableAsync(message, AbortToken))
            .Should()
            .BeFalse("deferral released this attempt");

        var deferred = (await _AdmitAsync(storage, envelope)).Message;
        var attempts = deferred.InlineAttempts++;
        (await storage.LeaseReceiveAndReserveAttemptAsync(deferred, TimeSpan.FromMinutes(5), attempts, AbortToken))
            .Should()
            .BeTrue();
        var nextFence = deferred.InboxAttemptFence!;
        nextFence.AttemptId.Should().NotBe(fence.AttemptId);
        deferred.InboxGeneration.Should().Be(message.InboxGeneration);
        (await storage.DeferReceivedInboxOrphanAsync(deferred, AbortToken))
            .Should()
            .BeTrue("a previously classified orphan must release every new probe");
        var repeated = (await _AdmitAsync(storage, envelope)).Message;
        repeated.LockedUntil.Should().BeNull();
        repeated.Owner.Should().BeNull();
        repeated.NextRetryAt.Should().BeOnOrAfter(message.NextRetryAt!.Value);
        attempts = repeated.InlineAttempts++;
        (await storage.LeaseReceiveAndReserveAttemptAsync(repeated, TimeSpan.FromMinutes(5), attempts, AbortToken))
            .Should()
            .BeTrue();
        (await storage.ConfirmReceivedInboxRoutableAsync(repeated, AbortToken)).Should().BeTrue();
        var recovered = (await _AdmitAsync(storage, envelope)).Message;
        recovered.IsInboxOrphaned.Should().BeFalse();
        recovered.InboxGeneration.Should().Be(message.InboxGeneration);
        recovered.InboxAttemptFence!.AttemptId.Should().NotBe(nextFence.AttemptId);
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_keep_orphan_probe_capacity_separate_from_ordinary_retries(MessageLane lane)
    {
        await using var provider = _CreateProvider(options =>
        {
            options.RetryBatchSize = 1;
            options.OrphanProbeBatchSize = 2;
        });
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var orphanIds = new List<Guid>();
        Guid ordinaryId = default;
        for (var i = 0; i < 5; i++)
        {
            var message = (await _AdmitAsync(storage, _CreateMessage(lane))).Message;
            message.InlineAttempts++;
            (await storage.LeaseReceiveAndReserveAttemptAsync(message, TimeSpan.FromMinutes(5), 0, AbortToken))
                .Should()
                .BeTrue();
            if (i < 4)
            {
                // Deferral releases the claim; re-lease so the due-time mutation below runs under a live fence.
                (await storage.DeferReceivedInboxOrphanAsync(message, AbortToken))
                    .Should()
                    .BeTrue();
                var deferredAttempts = message.InlineAttempts++;
                (
                    await storage.LeaseReceiveAndReserveAttemptAsync(
                        message,
                        TimeSpan.FromMinutes(5),
                        deferredAttempts,
                        AbortToken
                    )
                )
                    .Should()
                    .BeTrue();
                orphanIds.Add(message.StorageId);
            }
            else
            {
                ordinaryId = message.StorageId;
            }
            var identity = new MessageLeaseIdentity(
                message.StorageId,
                message.Owner,
                message.LockedUntil!.Value,
                lane,
                message.InboxAttemptFence
            );
            (await _MutateLeaseAsync(storage, "defer", identity, DateTimeOffset.UtcNow.AddMinutes(-10 + i)))
                .Should()
                .BeTrue();
        }

        var ordinary = (await storage.GetReceivedMessagesOfNeedRetryAsync(lane, AbortToken)).ToList();
        ordinary.Should().ContainSingle().Which.StorageId.Should().Be(ordinaryId);
        var probes = (await storage.GetReceivedInboxOrphansOfNeedRetryAsync(lane, AbortToken)).ToList();
        probes.Should().HaveCount(2);
        probes.Should().OnlyContain(message => orphanIds.Contains(message.StorageId) && message.IsInboxOrphaned);
        var oppositeLane = lane is MessageLane.Bus ? MessageLane.Queue : MessageLane.Bus;
        (await storage.GetReceivedInboxOrphansOfNeedRetryAsync(oppositeLane, AbortToken)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("release", MessageLane.Bus)]
    [InlineData("batch-release", MessageLane.Bus)]
    [InlineData("defer", MessageLane.Bus)]
    [InlineData("release", MessageLane.Queue)]
    [InlineData("batch-release", MessageLane.Queue)]
    [InlineData("defer", MessageLane.Queue)]
    public async Task should_require_complete_inbox_fence_for_release_and_deferral(string operation, MessageLane lane)
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var envelope = _CreateMessage(lane);
        var winner = (await _AdmitAsync(storage, envelope)).Message;
        var previousAttempts = winner.InlineAttempts++;
        (
            await storage.LeaseReceiveAndReserveAttemptAsync(
                winner,
                TimeSpan.FromMinutes(5),
                previousAttempts,
                AbortToken
            )
        )
            .Should()
            .BeTrue();
        var fence = winner.InboxAttemptFence!;
        fence.Should().NotBeNull();
        var identity = new MessageLeaseIdentity(winner.StorageId, winner.Owner, winner.LockedUntil!.Value, lane, fence);
        var nextRetryAt = winner.LockedUntil.Value.AddMinutes(1);
        foreach (var invalidFence in _InvalidFences(fence).Values)
        {
            (
                await _MutateLeaseAsync(
                    storage,
                    operation,
                    identity with
                    {
                        InboxAttemptFence = invalidFence,
                    },
                    nextRetryAt
                )
            )
                .Should()
                .BeFalse("every field of the inbox fence must match, even when the outer lease matches");
            var unchanged = (await _AdmitAsync(storage, envelope)).Message;
            unchanged.Owner.Should().Be(winner.Owner);
            unchanged.LockedUntil.Should().Be(winner.LockedUntil);
            unchanged.NextRetryAt.Should().Be(winner.NextRetryAt);
            unchanged.InboxAttemptFence.Should().Be(fence);
        }

        (await _MutateLeaseAsync(storage, operation, identity, nextRetryAt)).Should().BeTrue();
        var released = (await _AdmitAsync(storage, envelope)).Message;
        released.Owner.Should().BeNull();
        released.LockedUntil.Should().BeNull();
        released.NextRetryAt.Should().Be(operation == "defer" ? nextRetryAt : winner.NextRetryAt);
        (await _MutateLeaseAsync(storage, operation, identity, nextRetryAt)).Should().BeFalse();
    }

    [Theory]
    [InlineData(MessageLane.Bus, "missing")]
    [InlineData(MessageLane.Bus, "storageId")]
    [InlineData(MessageLane.Bus, "lane")]
    [InlineData(MessageLane.Bus, "generation")]
    [InlineData(MessageLane.Bus, "incarnation")]
    [InlineData(MessageLane.Bus, "attemptId")]
    [InlineData(MessageLane.Bus, "owner")]
    [InlineData(MessageLane.Bus, "lockedUntil")]
    [InlineData(MessageLane.Queue, "missing")]
    [InlineData(MessageLane.Queue, "storageId")]
    [InlineData(MessageLane.Queue, "lane")]
    [InlineData(MessageLane.Queue, "generation")]
    [InlineData(MessageLane.Queue, "incarnation")]
    [InlineData(MessageLane.Queue, "attemptId")]
    [InlineData(MessageLane.Queue, "owner")]
    [InlineData(MessageLane.Queue, "lockedUntil")]
    public async Task should_require_complete_inbox_fence_before_reserving_attempt(MessageLane lane, string field)
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var envelope = _CreateMessage(lane);
        var message = (await _AdmitAsync(storage, envelope)).Message;
        message.InlineAttempts++;
        (await storage.LeaseReceiveAndReserveAttemptAsync(message, TimeSpan.FromMinutes(5), 0, AbortToken))
            .Should()
            .BeTrue();
        var fence = message.InboxAttemptFence!;
        var originalAttempts = message.InlineAttempts++;
        message.InboxAttemptFence = _InvalidFences(fence)[field];
        (await storage.ReserveReceiveAttemptAsync(message, originalAttempts, AbortToken))
            .Should()
            .BeFalse("the incoming fence must match before changing the durable attempt counter");
        var unchanged = (await _AdmitAsync(storage, envelope)).Message;
        unchanged.InlineAttempts.Should().Be(originalAttempts);
        unchanged.InboxAttemptFence.Should().Be(fence);
        unchanged.LockedUntil.Should().Be(message.LockedUntil);
        unchanged.Owner.Should().Be(message.Owner);
        message.InboxAttemptFence = fence;
        (await storage.ReserveReceiveAttemptAsync(message, originalAttempts, AbortToken)).Should().BeTrue();
        (await _AdmitAsync(storage, envelope)).Message.InlineAttempts.Should().Be(message.InlineAttempts);
    }

    [Theory]
    [InlineData(MessageLane.Bus, false)]
    [InlineData(MessageLane.Queue, false)]
    [InlineData(MessageLane.Bus, true)]
    [InlineData(MessageLane.Queue, true)]
    public async Task should_preserve_attempt_fence_within_lease_and_rotate_on_fresh_lease(
        MessageLane lane,
        bool recoverThroughPickup
    )
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var envelope = _CreateMessage(lane);
        var message = (await _AdmitAsync(storage, envelope)).Message;
        message.InlineAttempts++;
        (await storage.LeaseReceiveAndReserveAttemptAsync(message, TimeSpan.FromMinutes(5), 0, AbortToken))
            .Should()
            .BeTrue();
        var originalFence = message.InboxAttemptFence!;
        var capturedLease = new MessageLeaseIdentity(
            message.StorageId,
            message.Owner,
            message.LockedUntil!.Value,
            lane,
            originalFence
        );

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var originalAttempts = message.InlineAttempts++;
            (await storage.ReserveReceiveAttemptAsync(message, originalAttempts, AbortToken)).Should().BeTrue();
            message.InboxAttemptFence.Should().Be(originalFence);
            var persisted = (await _AdmitAsync(storage, envelope)).Message;
            persisted.InlineAttempts.Should().Be(message.InlineAttempts);
            persisted.InboxAttemptFence.Should().Be(originalFence);
            persisted.LockedUntil.Should().Be(capturedLease.LockedUntil);
            (await storage.ReserveReceiveAttemptAsync(message, originalAttempts, AbortToken))
                .Should()
                .BeFalse("a stale counter cannot reserve again, even with the right fence");
        }

        // A claim-time identity must remain valid throughout the inline retry burst.
        (
            await _MutateLeaseAsync(
                storage,
                recoverThroughPickup ? "defer" : "release",
                capturedLease,
                DateTimeOffset.MinValue
            )
        )
            .Should()
            .BeTrue();
        var successor = (await _AdmitAsync(storage, envelope)).Message;
        if (recoverThroughPickup)
        {
            successor = (await storage.GetReceivedMessagesOfNeedRetryAsync(lane, AbortToken)).Single(candidate =>
                candidate.StorageId == message.StorageId
            );
        }
        else
        {
            var originalAttempts = successor.InlineAttempts++;
            (
                await storage.LeaseReceiveAndReserveAttemptAsync(
                    successor,
                    TimeSpan.FromMinutes(5),
                    originalAttempts,
                    AbortToken
                )
            )
                .Should()
                .BeTrue();
        }
        var successorFence = successor.InboxAttemptFence!;
        successorFence.AttemptId.Should().NotBe(originalFence.AttemptId);
        var nextAttempt = successor.InlineAttempts++;
        (await storage.ReserveReceiveAttemptAsync(successor, nextAttempt, AbortToken)).Should().BeTrue();
        successor.InboxAttemptFence.Should().Be(successorFence);
    }

    public static TheoryData<string, string?> InvalidIdentities =>
        new()
        {
            { "name", null },
            { "name", "" },
            { "name", " \t" },
            { "name", new string('n', 201) },
            { "consumerIdentity", null },
            { "consumerIdentity", "" },
            { "consumerIdentity", " \t" },
            { "consumerIdentity", new string('c', ConsumerMetadata.ConsumerIdentityMaxLength + 1) },
            { "contractVersion", null },
            { "contractVersion", "" },
            { "contractVersion", " \t" },
            { "contractVersion", new string('v', 101) },
            { "message.Origin.Id", null },
            { "message.Origin.Id", "" },
            { "message.Origin.Id", " \t" },
            { "message.Origin.Id", new string('i', MessageOptions.MessageIdMaxLength + 1) },
            { "message", new string('t', MessageOptions.TenantIdMaxLength + 1) },
        };

    [Theory]
    [MemberData(nameof(InvalidIdentities))]
    public async Task should_reject_invalid_inbox_identity(string parameter, string? value)
    {
        await using var provider = _CreateProvider();
        var storage = provider.GetRequiredService<IDataStorage>();
        var message = _CreateMessage(MessageLane.Bus);
        if (parameter == "message.Origin.Id")
        {
            message.Origin.Headers[Headers.MessageId] = value;
        }
        if (parameter == "message")
        {
            message.Origin.Headers[Headers.TenantId] = value;
        }
        var error = await Record.ExceptionAsync(async () =>
            await storage.AdmitReceivedMessageAsync(
                parameter == "name" ? value! : "orders.created",
                "group",
                parameter == "consumerIdentity" ? value! : "orders.consumer",
                parameter == "contractVersion" ? value! : "v1",
                message,
                cancellationToken: AbortToken
            )
        );
        error.Should().BeOfType<ArgumentException>().Which.ParamName.Should().Be(parameter);
    }

    [Fact]
    public async Task should_reject_negative_inbox_generation()
    {
        await using var provider = _CreateProvider();
        var error = await Record.ExceptionAsync(async () =>
            await _AdmitAsync(provider.GetRequiredService<IDataStorage>(), _CreateMessage(MessageLane.Bus), -1)
        );
        error.Should().BeOfType<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("generation");
    }

    [Fact]
    public async Task should_reject_unsupported_inbox_lane()
    {
        await using var provider = _CreateProvider();
        var error = await Record.ExceptionAsync(async () =>
            await _AdmitAsync(provider.GetRequiredService<IDataStorage>(), _CreateMessage((MessageLane)99))
        );
        error.Should().BeOfType<InvalidOperationException>();
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_accept_identity_length_boundaries_and_normalize_blank_tenants(MessageLane lane)
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var message = _CreateMessage(lane);
        message.Origin.Headers[Headers.MessageId] = new string('i', MessageOptions.MessageIdMaxLength);
        var name = new string('n', 200);
        var consumer = new string('c', ConsumerMetadata.ConsumerIdentityMaxLength);
        var version = new string('v', 100);
        ValueTask<InboxAdmissionResult> admit() =>
            storage.AdmitReceivedMessageAsync(
                name,
                "group",
                consumer,
                version,
                message,
                long.MaxValue,
                cancellationToken: AbortToken
            );
        var first = await admit();
        first.Disposition.Should().Be(InboxAdmissionDisposition.Winner);
        message.Origin.Headers[Headers.TenantId] = " \t";
        var blankTenant = await admit();
        blankTenant.Disposition.Should().Be(InboxAdmissionDisposition.InFlightDuplicate);
        blankTenant.Message.StorageId.Should().Be(first.Message.StorageId);
        message.Origin.Headers[Headers.TenantId] = new string('t', MessageOptions.TenantIdMaxLength);
        var tenant = await admit();
        tenant.Disposition.Should().Be(InboxAdmissionDisposition.Winner);
        tenant.Message.InboxKey!.TenantId.Should().Be(message.Origin.Headers[Headers.TenantId]);
        tenant.Message.StorageId.Should().NotBe(first.Message.StorageId);
    }

    private ServiceProvider _CreateProvider(Action<MessagingOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            ConfigureStorage(setup);
            if (configure is not null)
            {
                configure(setup.Options);
            }
        });
        return services.BuildServiceProvider();
    }

    private static MediumMessage _CreateMessage(MessageLane lane) =>
        new()
        {
            StorageId = Guid.Empty,
            Content = string.Empty,
            Lane = lane,
            Origin = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = Guid.NewGuid().ToString(),
                    [Headers.MessageName] = "orders.created",
                    [Headers.Group] = "group",
                },
                "payload"
            ),
        };

    private static ValueTask<InboxAdmissionResult> _AdmitAsync(
        IDataStorage storage,
        MediumMessage message,
        long generation = 0
    ) =>
        storage.AdmitReceivedMessageAsync(
            "orders.created",
            "group",
            "orders.consumer",
            "v1",
            message,
            generation,
            cancellationToken: AbortToken
        );

    private static async ValueTask<bool> _MutateLeaseAsync(
        IDataStorage storage,
        string operation,
        MessageLeaseIdentity identity,
        DateTimeOffset nextRetryAt
    ) =>
        operation switch
        {
            "release" => await ((IGracefulLeaseReleaseStorage)storage).ReleaseReceivedLeaseAsync(identity, AbortToken),
            "batch-release" => await ((IGracefulLeaseReleaseStorage)storage).ReleaseReceivedLeasesAsync(
                [identity],
                AbortToken
            ) == 1,
            "defer" => await ((ICircuitRetryDeferralStorage)storage).DeferReceivedRetryAsync(
                new(identity, nextRetryAt),
                AbortToken
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    private static Dictionary<string, InboxAttemptFence?> _InvalidFences(InboxAttemptFence fence) =>
        new(StringComparer.Ordinal)
        {
            ["missing"] = null,
            ["storageId"] = fence with { StorageId = Guid.NewGuid() },
            ["lane"] = fence with { Lane = fence.Lane is MessageLane.Bus ? MessageLane.Queue : MessageLane.Bus },
            ["generation"] = fence with { Generation = fence.Generation + 1 },
            ["incarnation"] = fence with { GenerationIncarnationId = Guid.NewGuid() },
            ["attemptId"] = fence with { AttemptId = Guid.NewGuid() },
            ["owner"] = fence with { Owner = "different-owner" },
            ["lockedUntil"] = fence with { LockedUntil = fence.LockedUntil.AddSeconds(1) },
        };
}
