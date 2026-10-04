// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class NatsConsumerClientFactoryTests : TestBase
{
    private readonly IOptions<NatsMessagingOptions> _options;
    private readonly IOptions<NatsMessagingOptions> _invalidUrlOptions;
    private readonly IServiceProvider _serviceProvider;

    public NatsConsumerClientFactoryTests()
    {
        _options = Options.Create(
            new NatsMessagingOptions
            {
                Servers = "nats://headless-framework-nats-test.invalid:4222",
                ConfigureConnection = opts =>
                    opts with
                    {
                        ConnectTimeout = TimeSpan.FromMilliseconds(100),
                        RetryOnInitialConnect = false,
                    },
            }
        );
        _invalidUrlOptions = Options.Create(
            new NatsMessagingOptions
            {
                // The NATS client throws UriFormatException building its options from this value, before it ever
                // observes the cancellation token: the deterministic connect failure the test needs.
                Servers = "nats://not a valid url",
            }
        );
        _serviceProvider = new ServiceCollection().BuildServiceProvider();
    }

    [Fact]
    public async Task should_wrap_connection_exception_in_broker_connection_exception()
    {
        var factory = new NatsConsumerClientFactory(_options, _serviceProvider);

        // ConnectAsync must fail without depending on local port state.
        var act = async () => await factory.CreateAsync(new ConsumerClientRequest("test-group", 1, MessageLane.Queue));

        var exception = await act.Should().ThrowAsync<BrokerConnectionException>();
        exception.Which.InnerException.Should().NotBeNull();
    }

    [Fact]
    public async Task should_create_every_instance_client_instead_of_refusing_it()
    {
        var factory = new NatsConsumerClientFactory(_options, _serviceProvider);
        var request = new ConsumerClientRequest(
            "billing.cache",
            1,
            MessageLane.Bus,
            ConsumerSubscriptionKind.EveryInstance,
            Guid.NewGuid()
        );

        // The unreachable server fails the connect, which proves the factory accepted the kind and got that far.
        var act = async () => await factory.CreateAsync(request, AbortToken);

        await act.Should().ThrowAsync<BrokerConnectionException>();
    }

    [Fact]
    public async Task should_preserve_factory_cancellation()
    {
        var factory = new NatsConsumerClientFactory(_options, _serviceProvider);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () =>
            await factory.CreateAsync(new ConsumerClientRequest("test-group", 1, MessageLane.Queue), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_preserve_factory_cancellation_when_connect_fails_first()
    {
        // given - a server URL the NATS client cannot parse, so without the early token observation the
        // connect attempt fails synchronously with its own exception (UriFormatException) before it ever
        // sees the cancelled token, and the caller's token is cancelled: whichever the client raises,
        // cancellation must win. (The real client has no injectable connect seam at the factory level, so
        // the unparseable URL is what makes the failure-first order deterministic instead of a race.)
        var factory = new NatsConsumerClientFactory(_invalidUrlOptions, _serviceProvider);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () =>
            await factory.CreateAsync(new ConsumerClientRequest("test-group", 1, MessageLane.Queue), cts.Token);

        // then
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public void should_implement_consumer_client_factory_interface()
    {
        var factory = new NatsConsumerClientFactory(_options, _serviceProvider);
        factory.Should().BeAssignableTo<IConsumerClientFactory>();
    }
}
