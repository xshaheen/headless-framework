// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class MessagingDistributedLockReleaseSignalTests : TestBase
{
    private const int _PublishFailedEventId = 17;

    private readonly IBus _bus = Substitute.For<IBus>();
    private readonly List<int> _eventIds = [];
    private readonly MessagingDistributedLockReleaseSignal _signal;

    public MessagingDistributedLockReleaseSignalTests()
    {
        var logger = Substitute.For<ILogger<MessagingDistributedLockReleaseSignal>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        logger
            .When(l =>
                l.Log(
                    Arg.Any<LogLevel>(),
                    Arg.Any<EventId>(),
                    Arg.Any<object>(),
                    Arg.Any<Exception?>(),
                    Arg.Any<Func<object, Exception?, string>>()
                )
            )
            .Do(call =>
            {
                lock (_eventIds)
                {
                    _eventIds.Add(call.Arg<EventId>().Id);
                }
            });
        _signal = new MessagingDistributedLockReleaseSignal(_bus, logger);
    }

    [Fact]
    public async Task should_publish_a_direct_system_message_without_the_callers_token()
    {
        // given
        var resource = Faker.Random.AlphaNumeric(10);
        var leaseId = Faker.Random.AlphaNumeric(10);

        // when
        _signal.Announce(resource, leaseId);

        // then - a lock released from host scope carries no tenant, so the signal is a system message
        await _bus.Received(1)
            .PublishAsync(
                Arg.Is<DistributedLockReleased>(m => m.Resource == resource && m.LeaseId == leaseId),
                Arg.Is<PublishOptions?>(o => o!.DeliveryMode == DeliveryMode.Direct && o.IsSystemMessage),
                CancellationToken.None
            );
    }

    [Fact]
    public void should_log_and_not_throw_when_the_publish_throws_synchronously()
    {
        // given - the bus dies when publishing; the wake-up is best-effort and waiters fall back to polling
        _bus.PublishAsync(Arg.Any<DistributedLockReleased>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns<PublishReceipt>(_ => throw new InvalidOperationException("bus down"));

        // when
        var act = () => _signal.Announce(Faker.Random.AlphaNumeric(10), Faker.Random.AlphaNumeric(10));

        // then
        act.Should().NotThrow();
        _EventIds().Should().ContainSingle().Which.Should().Be(_PublishFailedEventId);
    }

    [Fact]
    public void should_log_when_the_publish_completes_faulted()
    {
        // given - the bus returns an already-faulted publish instead of throwing
        _bus.PublishAsync(Arg.Any<DistributedLockReleased>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PublishReceipt>(new InvalidOperationException("bus down")));

        // when
        var act = () => _signal.Announce(Faker.Random.AlphaNumeric(10), Faker.Random.AlphaNumeric(10));

        // then
        act.Should().NotThrow();
        _EventIds().Should().ContainSingle().Which.Should().Be(_PublishFailedEventId);
    }

    [Fact]
    public async Task should_return_without_waiting_for_a_slow_broker_and_log_its_later_failure()
    {
        // given - the broker has not acknowledged the wake-up yet
        var publishGate = new TaskCompletionSource<PublishReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        _bus.PublishAsync(Arg.Any<DistributedLockReleased>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(publishGate.Task);

        // when
        _signal.Announce(Faker.Random.AlphaNumeric(10), Faker.Random.AlphaNumeric(10));

        // then - the call returned before the publish finished, and the later failure is still logged
        _EventIds().Should().BeEmpty();
        publishGate.SetException(new InvalidOperationException("bus down"));
        await _WaitForEventAsync(_PublishFailedEventId);
    }

    private List<int> _EventIds()
    {
        lock (_eventIds)
        {
            return [.. _eventIds];
        }
    }

    // The failure is logged by a continuation that runs on the thread pool, after the publish task faults.
    private async Task _WaitForEventAsync(int eventId)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!_EventIds().Contains(eventId))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }
}
