// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NATS.Client.Core;

namespace Tests;

// Every factory test injects the connect outcome, so none of them opens a socket or resolves a host name: the result
// is the same on every machine and in any order.
public sealed class NatsConsumerClientFactoryTests : TestBase
{
    private readonly IOptions<NatsMessagingOptions> _options = Options.Create(
        new NatsMessagingOptions { Servers = "nats://localhost:4222" }
    );

    private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task should_wrap_connection_exception_in_broker_connection_exception_when_connect_fails()
    {
        // given
        var failure = new NatsException("broker unreachable");
        var factory = _CreateFactory(_ => Task.FromException(failure));

        // when
        var act = async () => await factory.CreateAsync(_QueueRequest(), AbortToken);

        // then
        var exception = await act.Should().ThrowAsync<BrokerConnectionException>();
        exception.Which.InnerException.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task should_create_every_instance_client_when_connect_succeeds()
    {
        // given
        var factory = _CreateFactory(_ => Task.CompletedTask);
        var request = new ConsumerClientRequest(
            "billing.cache",
            1,
            MessageLane.Bus,
            ConsumerSubscriptionKind.EveryInstance,
            Guid.NewGuid()
        );

        // when
        var client = await factory.CreateAsync(request, AbortToken);

        // then
        await using var _ = client;
        client.Should().BeOfType<NatsConsumerClient>();
    }

    [Fact]
    public async Task should_not_connect_when_the_token_is_already_cancelled()
    {
        // given
        var connectCalls = 0;
        var factory = _CreateFactory(_ =>
        {
            Interlocked.Increment(ref connectCalls);
            return Task.CompletedTask;
        });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await factory.CreateAsync(_QueueRequest(), cts.Token);

        // then
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cts.Token);
        connectCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_preserve_factory_cancellation_when_connect_fails_after_the_token_is_cancelled()
    {
        // given - the caller cancels while the connect is in flight, and the connect then fails with the broker
        // client's own exception before anything observes the token: the failure must not outrank the cancellation.
        using var cts = new CancellationTokenSource();
        var factory = _CreateFactory(_ =>
        {
            cts.Cancel();
            return Task.FromException(new NatsException("connection reset"));
        });

        // when
        var act = async () => await factory.CreateAsync(_QueueRequest(), cts.Token);

        // then
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task should_surface_cancellation_when_the_token_is_cancelled_while_connect_is_pending()
    {
        // given - a connect that never completes on its own
        var connectStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = _CreateFactory(_ =>
        {
            connectStarted.TrySetResult();
            return new TaskCompletionSource().Task;
        });
        using var cts = new CancellationTokenSource();

        // when
        var create = factory.CreateAsync(_QueueRequest(), cts.Token);
        await connectStarted.Task.WaitAsync(AbortToken);
        await cts.CancelAsync();
        var act = async () => await create;

        // then
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task should_report_a_connect_timeout_as_a_broker_failure_when_the_caller_token_is_live()
    {
        // given - the broker client cancels its own connect (an internal timeout) while the caller's token is live
        using var clientTimeout = new CancellationTokenSource();
        await clientTimeout.CancelAsync();
        var factory = _CreateFactory(_ => Task.FromCanceled(clientTimeout.Token));

        // when
        var act = async () => await factory.CreateAsync(_QueueRequest(), AbortToken);

        // then
        var exception = await act.Should().ThrowAsync<BrokerConnectionException>();
        exception.Which.InnerException.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public void should_implement_consumer_client_factory_interface()
    {
        var factory = _CreateFactory(_ => Task.CompletedTask);
        factory.Should().BeAssignableTo<IConsumerClientFactory>();
    }

    private NatsConsumerClientFactory _CreateFactory(Func<NatsConnection, Task> connect)
    {
        return new NatsConsumerClientFactory(_options, _serviceProvider, connect);
    }

    private static ConsumerClientRequest _QueueRequest()
    {
        return new ConsumerClientRequest("test-group", 1, MessageLane.Queue);
    }
}
