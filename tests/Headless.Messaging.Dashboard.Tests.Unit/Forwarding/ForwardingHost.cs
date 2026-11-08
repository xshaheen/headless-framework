// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Tests.Module;

namespace Tests.Forwarding;

/// <summary>
/// Messaging nodes that forward to each other: each is a <see cref="MessagingCommandHost"/> whose forwarding client
/// reaches the other nodes through one <see cref="PeerNetwork"/>, and whose discovery lists what
/// <see cref="StaticDiscovery"/> holds.
/// </summary>
internal static class ForwardingHost
{
    /// <summary>The key every node of a test deployment shares.</summary>
    public static readonly string Key = new('k', MessagingForwardingOptions.MinimumKeyLength);

    public static Task<WebApplication> StartAsync(
        string nodeName,
        PeerNetwork network,
        StaticDiscovery discovery,
        TimeProvider time,
        RecordingOperatorLog log,
        CancellationToken cancellationToken,
        Action<MessagingDashboardOptions>? options = null,
        Action<IServiceCollection>? configure = null
    )
    {
        return MessagingCommandHost.StartAsync(
            time,
            log,
            cancellationToken,
            nodeName,
            configure: services =>
            {
                services.AddSingleton<INodeDiscoveryProvider>(discovery);
                services.PostConfigure<MessagingDashboardOptions>(module =>
                {
                    module.Forwarding.Key = Key;
                    options?.Invoke(module);
                });
                services
                    .AddHttpClient(ForwardingProtocol.ClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => network.ForCaller(nodeName));
                configure?.Invoke(services);
            }
        );
    }

    /// <summary>A discovery entry for <paramref name="name"/> at <paramref name="address"/>.</summary>
    public static Node Entry(string name, string address, string? id = null) =>
        new()
        {
            Id = id ?? name,
            Name = name,
            Address = address,
            Port = 0,
            Tags = string.Empty,
        };
}
