// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Processor;
using Headless.Testing.Tests;

namespace Tests.Processor;

public sealed class RetryDispatchAttemptTests : TestBase
{
    [Fact]
    public async Task should_release_exactly_once_when_try_start_races_abandoned_batch_release()
    {
        // Queued→Running (TryStart) races Queued→Abandoned (ReleaseAbandonedBatchAsync — the
        // shutdown-drain path) on the same attempt. Exactly one CAS may win; the lease must be
        // released exactly once regardless of the interleaving: by the batch when abandon wins,
        // or by CompleteAsync's Running→Completed transition when the dispatch wins. Both
        // interleavings are valid outcomes — the assertion is the single-release invariant,
        // conditioned on the observed winner, never on timing.
        for (var iteration = 0; iteration < 1000; iteration++)
        {
            var (storage, counter) = _CreateCountingReleaseStorage();
            var attempt = _CreateAttempt(storage);
            attempt.TryQueue().Should().BeTrue();

            var startTask = Task.Run(() => attempt.TryStart(), AbortToken);
            var abandonTask = Task.Run(
                () => RetryDispatchAttempt.ReleaseAbandonedBatchAsync([attempt]).AsTask(),
                AbortToken
            );
            await Task.WhenAll(startTask, abandonTask).WaitAsync(AbortToken);

            var started = await startTask;
            if (started)
            {
                counter
                    .Count.Should()
                    .Be(
                        0,
                        "the dispatch won the CAS in iteration {0}, so the abandoned batch releases nothing",
                        iteration
                    );
            }
            else
            {
                counter
                    .Count.Should()
                    .Be(1, "the abandoned batch won the CAS in iteration {0} and owns the single release", iteration);
            }

            await attempt.CompleteAsync();
            counter
                .Count.Should()
                .Be(1, "exactly one release must fire across the race and completion in iteration {0}", iteration);
        }
    }

    [Fact]
    public async Task should_release_once_when_abandon_claimed_called_twice()
    {
        var (storage, counter) = _CreateCountingReleaseStorage();
        var attempt = _CreateAttempt(storage);

        await attempt.AbandonClaimedAsync();
        await attempt.AbandonClaimedAsync();

        counter.Count.Should().Be(1, "the Claimed→Abandoned transition fires at most once");
    }

    [Fact]
    public async Task should_not_release_when_complete_follows_abandon_claimed()
    {
        var (storage, counter) = _CreateCountingReleaseStorage();
        var attempt = _CreateAttempt(storage);

        await attempt.AbandonClaimedAsync();
        await attempt.CompleteAsync();

        counter.Count.Should().Be(1, "an abandoned attempt is terminal and CompleteAsync must not release again");
    }

    [Fact]
    public async Task should_not_queue_after_start()
    {
        var (storage, counter) = _CreateCountingReleaseStorage();
        var attempt = _CreateAttempt(storage);

        attempt.TryStart().Should().BeTrue("the Claimed→Running fallback CAS accepts a never-queued attempt");
        attempt.TryQueue().Should().BeFalse("a running attempt can no longer be queued");

        await attempt.CompleteAsync();
        counter.Count.Should().Be(1);
    }

    [Fact]
    public void should_return_null_when_storage_lacks_graceful_release_capability()
    {
        var storage = Substitute.For<IDataStorage>();

        var attempt = RetryDispatchAttempt.TryCreate(storage, MessageType.Publish, _CreateLeasedMessage());

        attempt.Should().BeNull("without IGracefulLeaseReleaseStorage the caller must fall back to plain dispatch");
    }

    [Fact]
    public void should_return_null_when_message_has_no_lease()
    {
        var (storage, _) = _CreateCountingReleaseStorage();
        var message = _CreateLeasedMessage();
        message.LockedUntil = null;

        var attempt = RetryDispatchAttempt.TryCreate(storage, MessageType.Subscribe, message);

        attempt.Should().BeNull("a message without LockedUntil has no lease generation to release");
    }

    [Fact]
    public void should_capture_exact_lease_identity_when_created()
    {
        var (storage, _) = _CreateCountingReleaseStorage();
        var message = _CreateLeasedMessage();

        var attempt = RetryDispatchAttempt.TryCreate(storage, MessageType.Publish, message);

        attempt.Should().NotBeNull();
        attempt!
            .Identity.Should()
            .Be(new MessageLeaseIdentity(message.StorageId, message.Owner, message.LockedUntil!.Value, message.Lane));
    }

    private static RetryDispatchAttempt _CreateAttempt(IDataStorage storage)
    {
        var attempt = RetryDispatchAttempt.TryCreate(storage, MessageType.Publish, _CreateLeasedMessage());
        attempt.Should().NotBeNull();
        return attempt!;
    }

    private static MediumMessage _CreateLeasedMessage()
    {
        var storageId = Guid.NewGuid();
        var message = new Message(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { "headless-msg-id", storageId.ToString("D") },
            },
            value: new MessageValue("test@test.com", "User")
        );

        return new MediumMessage
        {
            StorageId = storageId,
            Origin = message,
            Content = JsonSerializer.Serialize(message),
            Lane = MessageLane.Bus,
            Owner = "node-a",
            LockedUntil = DateTimeOffset.UtcNow.AddMinutes(5),
            Retries = 1,
        };
    }

    private static (IDataStorage Storage, ReleaseCounter Counter) _CreateCountingReleaseStorage()
    {
        var counter = new ReleaseCounter();
        var storage = Substitute.For<IDataStorage, IGracefulLeaseReleaseStorage>();
        var releaser = (IGracefulLeaseReleaseStorage)storage;
        releaser
            .ReleasePublishedLeaseAsync(Arg.Any<MessageLeaseIdentity>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                counter.Add(1);
                return ValueTask.FromResult(true);
            });
        releaser
            .ReleaseReceivedLeaseAsync(Arg.Any<MessageLeaseIdentity>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                counter.Add(1);
                return ValueTask.FromResult(true);
            });
        releaser
            .ReleasePublishedLeasesAsync(
                Arg.Any<IReadOnlyCollection<MessageLeaseIdentity>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var released = call.Arg<IReadOnlyCollection<MessageLeaseIdentity>>().Count;
                counter.Add(released);
                return ValueTask.FromResult(released);
            });
        releaser
            .ReleaseReceivedLeasesAsync(
                Arg.Any<IReadOnlyCollection<MessageLeaseIdentity>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var released = call.Arg<IReadOnlyCollection<MessageLeaseIdentity>>().Count;
                counter.Add(released);
                return ValueTask.FromResult(released);
            });

        return (storage, counter);
    }

    /// <summary>Thread-safe release tally shared between the race participants and the assertions.</summary>
    private sealed class ReleaseCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Add(int releases)
        {
            _ = Interlocked.Add(ref _count, releases);
        }
    }
}
