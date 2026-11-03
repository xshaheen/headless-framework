// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class DistributedLockReleasedConsumerTests : TestBase
{
    [Fact]
    public async Task should_hand_the_release_to_every_listener()
    {
        // given - the lock, semaphore, and reader-writer providers each listen
        var listeners = new[]
        {
            Substitute.For<IDistributedLockReleaseListener>(),
            Substitute.For<IDistributedLockReleaseListener>(),
            Substitute.For<IDistributedLockReleaseListener>(),
        };
        var consumer = new DistributedLockReleasedConsumer(
            listeners,
            NullLogger<DistributedLockReleasedConsumer>.Instance
        );
        var message = new DistributedLockReleased(Faker.Random.AlphaNumeric(10), Faker.Random.AlphaNumeric(10));

        // when
        await consumer.ConsumeAsync(_Context(message), AbortToken);

        // then
        foreach (var listener in listeners)
        {
            listener.Received(1).OnReleased(message.Resource, message.LeaseId);
        }
    }

    [Fact]
    public async Task should_wake_no_listener_when_cancelled()
    {
        // given
        var listener = Substitute.For<IDistributedLockReleaseListener>();
        var consumer = new DistributedLockReleasedConsumer(
            [listener],
            NullLogger<DistributedLockReleasedConsumer>.Instance
        );
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // when
        var act = async () =>
            await consumer.ConsumeAsync(
                _Context(new DistributedLockReleased(Faker.Random.AlphaNumeric(10), Faker.Random.AlphaNumeric(10))),
                cancelled.Token
            );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        listener.DidNotReceiveWithAnyArgs().OnReleased(default!, default!);
    }

    private static ConsumeContext<DistributedLockReleased> _Context(DistributedLockReleased message)
    {
        return new ConsumeContext<DistributedLockReleased>
        {
            Lane = MessageLane.Bus,
            Message = message,
            MessageId = "message-1",
            CorrelationId = null,
            TenantId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            MessageName = DistributedLockReleased.MessageName,
        };
    }
}
