// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.InMemory;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

/// <summary>
/// Two hosts on one shared in-memory transport stand in for two processes of one application: an every-instance
/// consumer receives every message in both, while a competing consumer of the same shape receives it in one.
/// </summary>
public sealed class InMemoryEveryInstanceHostTests : TestBase
{
    [Fact]
    public async Task should_deliver_every_message_once_to_each_host_of_an_every_instance_consumer()
    {
        // given
        var transport = new MemoryQueue(NullLogger<MemoryQueue>.Instance);
        await using var first = await _StartHostAsync(transport);
        await using var second = await _StartHostAsync(transport);
        var bus = first.GetRequiredService<IBus>();

        // when
        await bus.PublishAsync(new RateChanged("EUR"), cancellationToken: AbortToken);
        await bus.PublishAsync(new RateChanged("USD"), cancellationToken: AbortToken);
        var firstProbe = first.GetRequiredService<HostProbe>();
        var secondProbe = second.GetRequiredService<HostProbe>();
        await _WaitUntilAsync(() =>
            firstProbe.EveryInstance.Count == 2
            && secondProbe.EveryInstance.Count == 2
            && firstProbe.Competing.Count + secondProbe.Competing.Count == 2
        );
        await Task.Delay(TimeSpan.FromMilliseconds(200), AbortToken);

        // then
        firstProbe.EveryInstance.Should().BeEquivalentTo(["EUR", "USD"]);
        secondProbe.EveryInstance.Should().BeEquivalentTo(["EUR", "USD"]);
        firstProbe
            .Competing.Concat(secondProbe.Competing)
            .Should()
            .BeEquivalentTo(["EUR", "USD"], "replicas of a competing identity share one copy");
    }

    private static async Task<ServiceProvider> _StartHostAsync(MemoryQueue transport)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HostProbe>();
        services.ConfigureMessaging(messaging => messaging.AddModule<RatesModule>());
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
        });

        // Registered last, so both hosts resolve the one transport instead of their own.
        services.AddSingleton(transport);

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        return provider;
    }

    private static async Task _WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }
}

public sealed record RateChanged(string Currency);

public sealed class HostProbe
{
    public ConcurrentQueue<string> EveryInstance { get; } = new();

    public ConcurrentQueue<string> Competing { get; } = new();
}

public sealed class RateCache(HostProbe probe) : IConsume<RateChanged>
{
    public ValueTask ConsumeAsync(ConsumeContext<RateChanged> context, CancellationToken cancellationToken)
    {
        probe.EveryInstance.Enqueue(context.Message.Currency);
        return ValueTask.CompletedTask;
    }
}

public sealed class RateProjection(HostProbe probe) : IConsume<RateChanged>
{
    public ValueTask ConsumeAsync(ConsumeContext<RateChanged> context, CancellationToken cancellationToken)
    {
        probe.Competing.Enqueue(context.Message.Currency);
        return ValueTask.CompletedTask;
    }
}

public sealed class RatesModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddBusConsumer<RateCache, RateChanged>("rates.cache", everyInstance: true, _Dispatch<RateCache>());
        catalog.AddBusConsumer<RateProjection, RateChanged>(
            "rates.projection",
            everyInstance: false,
            _Dispatch<RateProjection>()
        );
    }

    private static MessageConsumerDispatch _Dispatch<TConsumer>()
        where TConsumer : class, IConsume<RateChanged> =>
        static (services, context, cancellationToken) =>
            ActivatorUtilities
                .CreateInstance<TConsumer>(services)
                .ConsumeAsync((ConsumeContext<RateChanged>)context, cancellationToken);
}
