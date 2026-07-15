// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;
using Headless.Messaging.AzureServiceBus;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class AzureServiceBusClientPoolTests : TestBase
{
    private static readonly IOptions<AzureServiceBusMessagingOptions> _Options = Options.Create(
        new AzureServiceBusMessagingOptions
        {
            ConnectionString =
                "Endpoint=sb://mynamespace.servicebus.windows.net/;SharedAccessKeyName=myPolicy;SharedAccessKey=myKey",
        }
    );

    private static AzureServiceBusClientPool _CreatePool(ServiceBusClient client, Func<int> onClientCreated)
    {
        return new AzureServiceBusClientPool(
            NullLogger<AzureServiceBusClientPool>.Instance,
            _Options,
            _ =>
            {
                onClientCreated();
                return client;
            }
        );
    }

    private static ServiceBusClient _CreateClientSubstitute()
    {
        var client = Substitute.For<ServiceBusClient>();
        client.CreateSender(Arg.Any<string>()).Returns(_ => Substitute.For<ServiceBusSender>());

        return client;
    }

    [Fact]
    public async Task should_create_single_client_for_concurrent_first_use()
    {
        // given
        var creations = 0;
        var client = _CreateClientSubstitute();
        await using var pool = _CreatePool(client, () => Interlocked.Increment(ref creations));

        const int iterations = 1_000;
        const int distinctPaths = 50;
        var gate = new TaskCompletionSource();

        var tasks = Enumerable
            .Range(0, iterations)
            .Select(i =>
                Task.Run(
                    async () =>
                    {
                        await gate.Task;
                        return pool.GetSender($"topic-{i % distinctPaths}");
                    },
                    AbortToken
                )
            )
            .ToList();

        // when
        gate.SetResult();
        var senders = await Task.WhenAll(tasks);

        // then
        creations.Should().Be(1);
        senders.Distinct().Should().HaveCount(distinctPaths);
        client.Received(distinctPaths).CreateSender(Arg.Any<string>());
    }

    [Fact]
    public async Task should_share_sender_for_same_entity_path()
    {
        // given
        var client = _CreateClientSubstitute();
        await using var pool = _CreatePool(client, () => 0);

        // when
        var first = pool.GetSender("orders");
        var second = pool.GetSender("orders");

        // then
        first.Should().BeSameAs(second);
        client.Received(1).CreateSender("orders");
    }

    [Fact]
    public async Task should_dispose_materialized_senders_before_client()
    {
        // given
        var disposeOrder = new List<string>();
        var client = Substitute.For<ServiceBusClient>();

        client
            .CreateSender(Arg.Any<string>())
            .Returns(call =>
            {
                var entityPath = call.Arg<string>();
                var sender = Substitute.For<ServiceBusSender>();
                sender
                    .DisposeAsync()
                    .Returns(_ =>
                    {
                        disposeOrder.Add($"sender:{entityPath}");
                        return ValueTask.CompletedTask;
                    });

                return sender;
            });

        client
            .DisposeAsync()
            .Returns(_ =>
            {
                disposeOrder.Add("client");
                return ValueTask.CompletedTask;
            });

        var pool = _CreatePool(client, () => 0);
        _ = pool.GetSender("orders");
        _ = pool.GetSender("invoices");

        // when
        await pool.DisposeAsync();

        // then
        disposeOrder.Should().HaveCount(3);
        disposeOrder[^1].Should().Be("client");
        disposeOrder[..^1].Should().BeEquivalentTo("sender:orders", "sender:invoices");
    }

    [Fact]
    public async Task should_dispose_idempotently()
    {
        // given
        var client = _CreateClientSubstitute();
        var pool = _CreatePool(client, () => 0);
        _ = pool.GetSender("orders");

        // when
        await pool.DisposeAsync();
        await pool.DisposeAsync();

        // then
        await client.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task should_dispose_safely_when_nothing_materialized()
    {
        // given
        var creations = 0;
        var pool = _CreatePool(_CreateClientSubstitute(), () => Interlocked.Increment(ref creations));

        // when
        var act = async () => await pool.DisposeAsync();

        // then
        await act.Should().NotThrowAsync();
        creations.Should().Be(0);
    }

    [Fact]
    public async Task should_not_poison_other_destinations_when_sender_creation_fails()
    {
        // given
        var client = Substitute.For<ServiceBusClient>();
        var goodSender = Substitute.For<ServiceBusSender>();
        client.CreateSender("bad").Throws(new ServiceBusException("boom", ServiceBusFailureReason.ServiceBusy));
        client.CreateSender("good").Returns(goodSender);
        await using var pool = _CreatePool(client, () => 0);

        // when
        var act = () => pool.GetSender("bad");

        // then
        act.Should().Throw<ServiceBusException>();
        pool.GetSender("good").Should().BeSameAs(goodSender);
    }

    [Fact]
    public async Task should_retry_same_destination_after_sender_creation_failure()
    {
        // given
        var client = Substitute.For<ServiceBusClient>();
        var sender = Substitute.For<ServiceBusSender>();
        var calls = 0;
        client
            .CreateSender("orders")
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new ServiceBusException("transient", ServiceBusFailureReason.ServiceBusy);
                }

                return sender;
            });

        await using var pool = _CreatePool(client, () => 0);

        var firstAttempt = () => pool.GetSender("orders");
        firstAttempt.Should().Throw<ServiceBusException>();

        // when
        var second = pool.GetSender("orders");

        // then
        second.Should().BeSameAs(sender);
    }

    [Fact]
    public async Task should_dispose_client_even_when_sender_creation_failed()
    {
        // given
        var client = Substitute.For<ServiceBusClient>();
        client
            .CreateSender(Arg.Any<string>())
            .Throws(new ServiceBusException("boom", ServiceBusFailureReason.ServiceBusy));
        var pool = _CreatePool(client, () => 0);

        var act = () => pool.GetSender("orders");
        act.Should().Throw<ServiceBusException>();

        // when
        await pool.DisposeAsync();

        // then: the client created during the failed attempt is not leaked
        await client.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task should_dispose_client_even_when_sender_dispose_throws()
    {
        // given: a materialized sender whose DisposeAsync faults during the drain
        var client = Substitute.For<ServiceBusClient>();
        var badSender = Substitute.For<ServiceBusSender>();
        badSender.DisposeAsync().Returns(ValueTask.FromException(new InvalidOperationException("close failed")));
        client.CreateSender("orders").Returns(badSender);
        var pool = _CreatePool(client, () => 0);
        _ = pool.GetSender("orders");

        // when
        var act = async () => await pool.DisposeAsync();

        // then: the sender fault propagates, but the client is still disposed (disposal is
        // one-shot, so skipping it here would leak the AMQP connection forever)
        await act.Should().ThrowAsync<InvalidOperationException>();
        await client.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task should_dispose_client_exactly_once_under_concurrent_dispose()
    {
        // given
        var client = _CreateClientSubstitute();
        var pool = _CreatePool(client, () => 0);
        _ = pool.GetSender("orders");

        // when: two dispose calls race
        await Task.WhenAll(pool.DisposeAsync().AsTask(), pool.DisposeAsync().AsTask());

        // then
        await client.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task should_not_leak_client_created_concurrently_with_dispose()
    {
        // given: a client factory blocked mid-creation (holding the pool's creation lock)
        var factoryEntered = new TaskCompletionSource();
        var releaseFactory = new TaskCompletionSource();
        var client = _CreateClientSubstitute();

        var pool = new AzureServiceBusClientPool(
            NullLogger<AzureServiceBusClientPool>.Instance,
            _Options,
            _ =>
            {
                factoryEntered.SetResult();
                releaseFactory.Task.Wait(AbortToken);
                return client;
            }
        );

        var getSenderTask = Task.Run(() => pool.GetSender("orders"), AbortToken);
        await factoryEntered.Task;

        // when: dispose races the in-flight creation, then the factory completes
        var disposeTask = Task.Run(async () => await pool.DisposeAsync(), AbortToken);
        releaseFactory.SetResult();
        _ = await getSenderTask;
        await disposeTask;

        // then: the client created during the race is still disposed — never leaked
        await client.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task should_complete_dispose_while_sender_creation_in_flight()
    {
        // given: client already materialized; a second sender blocked inside CreateSender
        var client = Substitute.For<ServiceBusClient>();
        var warmupSender = Substitute.For<ServiceBusSender>();
        client.CreateSender("warmup").Returns(warmupSender);

        var createEntered = new TaskCompletionSource();
        var releaseCreate = new TaskCompletionSource();
        var lateSender = Substitute.For<ServiceBusSender>();
        client
            .CreateSender("blocked")
            .Returns(_ =>
            {
                createEntered.SetResult();
                releaseCreate.Task.Wait(AbortToken);
                return lateSender;
            });

        var pool = _CreatePool(client, () => 0);
        _ = pool.GetSender("warmup");

        var blockedTask = Task.Run(() => pool.GetSender("blocked"), AbortToken);
        await createEntered.Task;

        // when: dispose runs while the sender is mid-creation
        var disposeTask = pool.DisposeAsync().AsTask();
        var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(5), AbortToken));

        // then: dispose skips the unmaterialized lazy instead of blocking on it, drains the
        // warmed sender, and disposes the client
        completed.Should().BeSameAs(disposeTask);
        releaseCreate.SetResult();
        _ = await blockedTask;
        await disposeTask;
        await warmupSender.Received(1).DisposeAsync();
        await client.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task should_throw_when_used_after_dispose()
    {
        // given
        var pool = _CreatePool(_CreateClientSubstitute(), () => 0);
        await pool.DisposeAsync();

        // when
        var act = () => pool.GetSender("orders");

        // then
        act.Should().Throw<ObjectDisposedException>();
    }
}
