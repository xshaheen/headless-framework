// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class NatsConsumerClientFactoryTests : TestBase
{
    private readonly IOptions<NatsMessagingOptions> _options;
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
        _serviceProvider = new ServiceCollection().BuildServiceProvider();
    }

    [Fact]
    public async Task should_wrap_connection_exception_in_broker_connection_exception()
    {
        var factory = new NatsConsumerClientFactory(_options, _serviceProvider);

        // ConnectAsync must fail without depending on local port state.
        var act = async () => await factory.CreateAsync("test-group", 1);

        var exception = await act.Should().ThrowAsync<BrokerConnectionException>();
        exception.Which.InnerException.Should().NotBeNull();
    }

    [Fact]
    public async Task should_preserve_factory_cancellation()
    {
        var factory = new NatsConsumerClientFactory(_options, _serviceProvider);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await factory.CreateAsync("test-group", 1, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void should_implement_consumer_client_factory_interface()
    {
        var factory = new NatsConsumerClientFactory(_options, _serviceProvider);
        factory.Should().BeAssignableTo<IConsumerClientFactory>();
    }
}
