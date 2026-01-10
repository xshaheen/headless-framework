// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Messaging;
using Framework.Testing.Tests;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class MassTransitMessageBusAdapterTests : TestBase
{
    [Fact]
    public async Task should_publish_message_with_headers()
    {
        await using var provider = _CreateProvider(harness: true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();
        var correlationId = Guid.NewGuid();

        await publisher.PublishAsync(
            new TestMessage("test"),
            new PublishMessageOptions
            {
                UniqueId = Guid.NewGuid(),
                CorrelationId = correlationId,
                Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["tenant"] = "acme" },
            },
            AbortToken
        );

        (await harness.Published.Any<TestMessage>()).Should().BeTrue();
        var context = (await harness.Published.SelectAsync<TestMessage>().First()).Context;
        context.CorrelationId.Should().Be(correlationId);

        await harness.Stop(AbortToken);
    }

    [Fact]
    public async Task should_receive_message_via_subscribe_async()
    {
        await using var provider = _CreateProvider(harness: false);

        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync(AbortToken);

        await using var scope = provider.CreateAsyncScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        IMessageSubscribeMedium<TestMessage>? received = null;
        var tcs = new TaskCompletionSource<bool>();

        await messageBus.SubscribeAsync<TestMessage>(
            (medium, ct) =>
            {
                received = medium;
                tcs.SetResult(true);
                return Task.CompletedTask;
            },
            AbortToken
        );

        var correlationId = Guid.NewGuid();
        await messageBus.PublishAsync(
            new TestMessage("test-value"),
            new PublishMessageOptions { UniqueId = Guid.NewGuid(), CorrelationId = correlationId },
            AbortToken
        );

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        received.Should().NotBeNull();
        received!.Payload.Value.Should().Be("test-value");
        received.CorrelationId.Should().Be(correlationId);

        await bus.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_cleanup_subscription_on_cancellation()
    {
        await using var provider = _CreateProvider(harness: false);

        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync(AbortToken);

        await using var scope = provider.CreateAsyncScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        using var cts = new CancellationTokenSource();

        await messageBus.SubscribeAsync<TestMessage>((_, _) => Task.CompletedTask, cts.Token);

        // Cancel should cleanup subscription
        await cts.CancelAsync();
        await Task.Delay(100, AbortToken); // Allow cleanup to complete

        // Should be able to subscribe again after cancellation
        await messageBus.SubscribeAsync<TestMessage>((_, _) => Task.CompletedTask, AbortToken);

        await bus.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_throw_when_already_subscribed_to_same_type()
    {
        await using var provider = _CreateProvider(harness: false);

        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync(AbortToken);

        await using var scope = provider.CreateAsyncScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await messageBus.SubscribeAsync<TestMessage>((_, _) => Task.CompletedTask, AbortToken);

        var act = async () => await messageBus.SubscribeAsync<TestMessage>((_, _) => Task.CompletedTask, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Already subscribed*");

        await bus.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_throw_when_disposed()
    {
        await using var provider = _CreateProvider(harness: false);

        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync(AbortToken);

        await using var scope = provider.CreateAsyncScope();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await ((IAsyncDisposable)messageBus).DisposeAsync();

        var act = async () => await messageBus.PublishAsync(new TestMessage("test"));

        await act.Should().ThrowAsync<ObjectDisposedException>();

        await bus.StopAsync(AbortToken);
    }

    private ServiceProvider _CreateProvider(bool harness)
    {
        var services = new ServiceCollection();
        services.AddSingleton(LoggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<IGuidGenerator, SequentialAsStringGuidGenerator>();

        if (harness)
        {
            services.AddMassTransitTestHarness(x => x.UsingInMemory());
        }
        else
        {
            services.AddMassTransit(x => x.UsingInMemory((ctx, cfg) => cfg.ConfigureEndpoints(ctx)));
        }

        services.AddHeadlessMassTransitAdapter();

        return services.BuildServiceProvider(true);
    }
}

public sealed record TestMessage(string Value);
