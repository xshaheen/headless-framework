// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// A host that bridges two brokers: the in-memory transport is its default one and a real RabbitMQ is a named transport
/// beside it. Each publishes and consumes on its own, and one broker being down does not stop the other.
/// </summary>
[Collection<RabbitMqFixture>]
public sealed class RabbitMqNamedTransportTests(RabbitMqFixture fixture) : TestBase
{
    private const string _Partner = "partner";
    private const string _Sentinel = "sentinel";

    [Fact]
    public async Task should_publish_and_consume_on_each_broker_independently()
    {
        // given
        await using var host = _BuildHost(_ConfigureReachablePartner);
        await host.ProvisionResourcesAsync(AbortToken);
        await host.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        var probe = host.GetRequiredService<BridgeProbe>();
        var sentinels = probe.WaitForAsync(probe.Internal, _Sentinel, probe.Partner, _Sentinel);

        // when
        await host.GetRequiredService<IBus>().PublishAsync(new PartnerOrderShipped("internal"), AbortToken);
        await host.GetRequiredKeyedService<IBus>(_Partner).PublishAsync(new PartnerOrderShipped("partner"), AbortToken);

        // A sentinel published on each broker after the others arrives behind any message still in flight, so waiting
        // for both gives a message delivered through the wrong broker the time to show up.
        await host.GetRequiredService<IBus>().PublishAsync(new PartnerOrderShipped(_Sentinel), AbortToken);
        await host.GetRequiredKeyedService<IBus>(_Partner).PublishAsync(new PartnerOrderShipped(_Sentinel), AbortToken);
        await sentinels.Bounded();

        // then
        probe.Internal.Should().BeEquivalentTo(["internal", _Sentinel]);
        probe.Partner.Should().BeEquivalentTo(["partner", _Sentinel]);
    }

    [Fact]
    public async Task should_keep_consuming_on_the_default_broker_while_the_named_broker_is_unreachable()
    {
        // given: the partner broker points at a port nothing listens on, so every connect to it is refused
        await using var host = _BuildHost(setup =>
            setup
                .AddTransport(_Partner)
                .UseRabbitMq(options =>
                {
                    options.HostName = "127.0.0.1";
                    options.Port = 1;
                    options.UserName = fixture.UserName;
                    options.Password = fixture.Password;
                })
        );
        await host.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        var probe = host.GetRequiredService<BridgeProbe>();
        var delivered = probe.WaitForAsync(probe.Internal, "internal", probe.Internal, "internal");

        // when: a publish through the unreachable broker is stored for the relay, and one through the default broker
        // goes on as if the other broker did not exist
        var stranded = await host.GetRequiredKeyedService<IBus>(_Partner)
            .PublishAsync(new PartnerOrderShipped("partner"), AbortToken);
        await host.GetRequiredService<IBus>().PublishAsync(new PartnerOrderShipped("internal"), AbortToken);
        await delivered.Bounded();

        // then
        host.GetRequiredService<IBootstrapper>().IsStarted.Should().BeTrue();
        stranded.StorageId.Should().NotBeNull("the outbox keeps the message until the partner broker is back");
        probe.Partner.Should().BeEmpty();
    }

    private void _ConfigureReachablePartner(MessagingSetupBuilder setup)
    {
        setup
            .AddTransport(_Partner)
            .UseRabbitMq(options =>
            {
                options.HostName = fixture.HostName;
                options.VirtualHost = fixture.VirtualHost;
                options.Port = fixture.Port;
                options.UserName = fixture.UserName;
                options.Password = fixture.Password;
                options.ExchangeName = $"named-transport-{Guid.NewGuid():N}";
            });
    }

    private static ServiceProvider _BuildHost(Action<MessagingSetupBuilder> configurePartner)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<BridgeProbe>();
        services.ConfigureMessaging(messaging => messaging.AddModule<BridgeModule>());
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
            configurePartner(setup);
            setup.Tune(PartnerOrderSync.Identity, consumer => consumer.Transport(_Partner));
        });

        return services.BuildServiceProvider();
    }
}

public sealed record PartnerOrderShipped(string OrderId);

public sealed class BridgeProbe
{
    private readonly Lock _gate = new();
    private readonly List<(ConcurrentQueue<string> Queue, string Value, TaskCompletionSource Done)> _waits = [];

    public ConcurrentQueue<string> Internal { get; } = new();

    public ConcurrentQueue<string> Partner { get; } = new();

    public void Record(ConcurrentQueue<string> target, string orderId)
    {
        target.Enqueue(orderId);

        lock (_gate)
        {
            foreach (var wait in _waits.Where(w => w.Queue.Contains(w.Value, StringComparer.Ordinal)))
            {
                wait.Done.TrySetResult();
            }
        }
    }

    /// <summary>Completes once <paramref name="first"/> holds one value and <paramref name="second"/> the other.</summary>
    public Task WaitForAsync(
        ConcurrentQueue<string> first,
        string firstValue,
        ConcurrentQueue<string> second,
        string secondValue
    ) => Task.WhenAll(_WaitFor(first, firstValue), _WaitFor(second, secondValue));

    private Task _WaitFor(ConcurrentQueue<string> queue, string value)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            _waits.Add((queue, value, done));
            if (queue.Contains(value, StringComparer.Ordinal))
            {
                done.TrySetResult();
            }
        }

        return done.Task;
    }
}

public sealed class InternalOrderProjection(BridgeProbe probe) : IConsume<PartnerOrderShipped>
{
    public const string Identity = "bridge.internal-projection";

    public ValueTask ConsumeAsync(ConsumeContext<PartnerOrderShipped> context, CancellationToken cancellationToken)
    {
        probe.Record(probe.Internal, context.Message.OrderId);
        return ValueTask.CompletedTask;
    }
}

public sealed class PartnerOrderSync(BridgeProbe probe) : IConsume<PartnerOrderShipped>
{
    public const string Identity = "bridge.partner-sync";

    public ValueTask ConsumeAsync(ConsumeContext<PartnerOrderShipped> context, CancellationToken cancellationToken)
    {
        probe.Record(probe.Partner, context.Message.OrderId);
        return ValueTask.CompletedTask;
    }
}

public sealed class BridgeModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddBusConsumer<InternalOrderProjection, PartnerOrderShipped>(
            InternalOrderProjection.Identity,
            everyInstance: false,
            _Dispatch<InternalOrderProjection>()
        );
        catalog.AddBusConsumer<PartnerOrderSync, PartnerOrderShipped>(
            PartnerOrderSync.Identity,
            everyInstance: false,
            _Dispatch<PartnerOrderSync>()
        );
    }

    private static MessageConsumerDispatch _Dispatch<TConsumer>()
        where TConsumer : class, IConsume<PartnerOrderShipped> =>
        static (services, context, cancellationToken) =>
            ActivatorUtilities
                .CreateInstance<TConsumer>(services)
                .ConsumeAsync((ConsumeContext<PartnerOrderShipped>)context, cancellationToken);
}
