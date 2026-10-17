// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Sockets;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// Stops the broker under a running consuming host and checks that the host reports it unreachable, then reachable
/// again once the broker is back. Each leaf owns an isolated broker it can interrupt without disturbing other tests.
/// </summary>
[PublicAPI]
public abstract class TransportConnectivityTestsBase : TestBase
{
    /// <summary>Gets how long the host may take to notice the outage.</summary>
    protected virtual TimeSpan OutageDetectionTimeout => TimeSpan.FromMinutes(2);

    /// <summary>Gets how long the host may take to notice the broker is back, a watchdog rebuild included.</summary>
    protected virtual TimeSpan RecoveryTimeout => TimeSpan.FromMinutes(2);

    /// <summary>Gets the lane the probe consumer listens on.</summary>
    protected virtual MessageLane ProbeLane => MessageLane.Bus;

    /// <summary>Gets the broker name of the message the probe consumer subscribes to, unique per test.</summary>
    protected string ProbeMessageName { get; } = $"connectivity-{Guid.NewGuid():N}";

    /// <summary>Starts the leaf's isolated broker.</summary>
    protected abstract ValueTask StartBrokerAsync(CancellationToken cancellationToken);

    /// <summary>Points the host's transport at the isolated broker.</summary>
    protected abstract void ConfigureTransport(MessagingSetupBuilder setup);

    /// <summary>Makes the broker unreachable while keeping its state for <see cref="RestoreBrokerAsync"/>.</summary>
    protected abstract Task InterruptBrokerAsync(CancellationToken cancellationToken);

    /// <summary>Makes the broker reachable again at the address the host already uses.</summary>
    protected abstract Task RestoreBrokerAsync(CancellationToken cancellationToken);

    /// <summary>Releases the isolated broker.</summary>
    protected abstract ValueTask StopBrokerAsync();

    public virtual async Task should_report_the_broker_unreachable_while_it_is_down_and_reachable_once_it_is_back()
    {
        await StartBrokerAsync(AbortToken);

        try
        {
            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.AddProvider(LoggerProvider);
                builder.SetMinimumLevel(LogLevel.Information);
            });
            services.ConfigureMessaging(messaging => messaging.Message<ConnectivityProbeMessage>(ProbeMessageName));
            services.AddHeadlessMessaging(setup =>
            {
                ConfigureTransport(setup);
                setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
                setup.UseInMemoryStorage();
                if (ProbeLane is MessageLane.Queue)
                {
                    setup.AddModule<ConnectivityProbeQueueModule>();
                }
                else
                {
                    setup.AddModule<ConnectivityProbeBusModule>();
                }
            });
            services.AddSingleton<ConnectivityProbeConsumer>();

            await using var provider = services.BuildServiceProvider();
            var bootstrapper = provider.GetRequiredService<IBootstrapper>();
            var connectivity = provider.GetRequiredService<TransportConnectivity>();
            await provider.SetupResourcesAsync(AbortToken);
            await bootstrapper.BootstrapAsync(AbortToken);
            connectivity.IsReachable.Should().BeTrue("the host just reached the broker to subscribe");

            await InterruptBrokerAsync(AbortToken);
            await _WaitUntilAsync(() => !connectivity.IsReachable, OutageDetectionTimeout, "the broker is down");

            await RestoreBrokerAsync(AbortToken);
            await _WaitUntilAsync(() => connectivity.IsReachable, RecoveryTimeout, "the broker is back");

            await bootstrapper.DisposeAsync();
        }
        finally
        {
            await StopBrokerAsync();
        }
    }

    private static async Task _WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string because)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        deadline.CancelAfter(timeout);

        try
        {
            while (!condition())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!AbortToken.IsCancellationRequested)
        {
            condition().Should().BeTrue($"{because}, and {timeout} passed without the host reporting it");
        }
    }
}

[PublicAPI]
public sealed record ConnectivityProbeMessage;

[PublicAPI]
public sealed class ConnectivityProbeConsumer : IConsume<ConnectivityProbeMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<ConnectivityProbeMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

/// <summary>Registers the probe as a competing consumer on the Bus lane.</summary>
public sealed class ConnectivityProbeBusModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddBusConsumer<ConnectivityProbeConsumer, ConnectivityProbeMessage>(
            "tests.transport-connectivity.probe",
            everyInstance: false,
            TestConsumerDispatch.FromServices<ConnectivityProbeConsumer, ConnectivityProbeMessage>()
        );
    }
}

/// <summary>Registers the probe as a consumer on the Queue lane.</summary>
public sealed class ConnectivityProbeQueueModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddQueueConsumer<ConnectivityProbeConsumer, ConnectivityProbeMessage>(
            "tests.transport-connectivity.queue-probe",
            TestConsumerDispatch.FromServices<ConnectivityProbeConsumer, ConnectivityProbeMessage>()
        );
    }
}

/// <summary>
/// Picks a free host port for a broker container that a test stops and starts: Docker assigns a new random port on
/// every start unless the binding names one, and the host under test keeps the address it was given.
/// </summary>
[PublicAPI]
public static class BrokerHostPort
{
    public static int Reserve()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
