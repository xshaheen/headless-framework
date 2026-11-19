// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

/// <summary>
/// One broker the host publishes to and consumes from: the default transport or a named one. It holds what is the
/// broker's own, so an outage of one broker does not read as an outage of another.
/// </summary>
internal sealed class MessagingTransport
{
    private readonly Lazy<MessagingCapabilityModel> _capabilities;
    private readonly Lazy<IBusTransport?> _bus;
    private readonly Lazy<IQueueTransport?> _queue;
    private readonly Lazy<IConsumerClientFactory> _consumerClientFactory;
    private readonly Lazy<IReadOnlyList<ITransportTopologyProvisioner>> _provisioners;

    private MessagingTransport(
        string? name,
        TransportConnectivity connectivity,
        Lazy<MessagingCapabilityModel> capabilities,
        Func<IBusTransport?> bus,
        Func<IQueueTransport?> queue,
        Func<IConsumerClientFactory> consumerClientFactory,
        Func<IReadOnlyList<ITransportTopologyProvisioner>> provisioners
    )
    {
        Name = name;
        Connectivity = connectivity;
        _capabilities = capabilities;
        _bus = new(bus);
        _queue = new(queue);
        _consumerClientFactory = new(consumerClientFactory);
        _provisioners = new(provisioners);
    }

    /// <summary>The name of a named transport; <see langword="null"/> for the default transport.</summary>
    public string? Name { get; }

    /// <summary>Whether this is the default transport.</summary>
    public bool IsDefault => Name is null;

    /// <summary>The name logs, health checks, and the manifest show: the transport's name, or the default transport's reserved name.</summary>
    public string DisplayName => Name ?? MessagingSetupBuilder.DefaultTransportName;

    /// <summary>Whether this broker is reachable, fed by this transport's own sends and receives.</summary>
    public TransportConnectivity Connectivity { get; }

    /// <summary>
    /// What this broker supports, composed with the host's storage and coordination: publishes through this transport
    /// are checked against it.
    /// </summary>
    public MessagingCapabilityModel Capabilities => _capabilities.Value;

    public IBusTransport? Bus => _bus.Value;

    public IQueueTransport? Queue => _queue.Value;

    public IConsumerClientFactory ConsumerClientFactory => _consumerClientFactory.Value;

    public IReadOnlyList<ITransportTopologyProvisioner> Provisioners => _provisioners.Value;

    /// <summary>The transport that sends on <paramref name="lane"/>, or <see langword="null"/> when none is registered.</summary>
    public ITransport? FindTransport(MessageLane lane) =>
        lane switch
        {
            MessageLane.Bus => Bus,
            MessageLane.Queue => Queue,
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, "A defined messaging lane is required."),
        };

    /// <summary>The transport that sends on <paramref name="lane"/>.</summary>
    /// <exception cref="InvalidOperationException">No transport is registered for <paramref name="lane"/>.</exception>
    public ITransport GetTransport(MessageLane lane) =>
        FindTransport(lane)
        ?? throw new InvalidOperationException(
            $"Transport '{DisplayName}' registers no {lane} transport, so it cannot send on the {lane} lane."
        );

    internal static MessagingTransport CreateDefault(IServiceProvider services) =>
        new(
            name: null,
            services.GetRequiredService<TransportConnectivity>(),
            new(services.GetRequiredService<MessagingCapabilityModel>),
            services.GetService<IBusTransport>,
            services.GetService<IQueueTransport>,
            services.GetRequiredService<IConsumerClientFactory>,
            () => [.. services.GetServices<ITransportTopologyProvisioner>()]
        );

    internal static MessagingTransport CreateNamed(IServiceProvider services, string name) =>
        new(
            name,
            new TransportConnectivity(),
            new(() =>
                MessagingCapabilityModel.Compose([
                    // The named transport's own contribution beside the host's shared storage and coordination: the
                    // default transport's contribution must not make this broker look capable of what it is not.
                    .. services.GetKeyedServices<MessagingProviderCapabilities>(name),
                    .. services
                        .GetServices<MessagingProviderCapabilities>()
                        .Where(static capability => capability.Role is not MessagingProviderRole.Transport),
                ])
            ),
            () => services.GetKeyedService<IBusTransport>(name),
            () => services.GetKeyedService<IQueueTransport>(name),
            () => services.GetRequiredKeyedService<IConsumerClientFactory>(name),
            () => [.. services.GetKeyedServices<ITransportTopologyProvisioner>(name)]
        );
}
