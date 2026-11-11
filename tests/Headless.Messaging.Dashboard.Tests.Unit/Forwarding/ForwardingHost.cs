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

    // The test server's client resolves a relative request against this address, so a call signed here is signed for
    // the target the node receives.
    private static readonly Uri _TestServerAddress = new("http://localhost/");

    /// <summary>
    /// Signs <paramref name="request"/> as a node holding <paramref name="key"/> (default <see cref="Key"/>) would, at
    /// <paramref name="time"/>'s now. Call it after the hop, actor, access headers and the body are set.
    /// </summary>
    public static Task SignAsync(
        HttpRequestMessage request,
        TimeProvider time,
        CancellationToken cancellationToken,
        string? key = null
    )
    {
        if (request.RequestUri is { IsAbsoluteUri: false } relative)
        {
            request.RequestUri = new Uri(_TestServerAddress, relative);
        }

        return ForwardingSignature.SignAsync(request, key ?? Key, time, cancellationToken);
    }

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
