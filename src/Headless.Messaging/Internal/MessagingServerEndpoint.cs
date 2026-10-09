// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Messaging.Internal;

/// <summary>
/// The <c>server.address</c> and <c>server.port</c> values derived from a <see cref="BrokerAddress.Endpoint"/>.
/// </summary>
/// <param name="Address">The host name or IP of the first broker in the endpoint, or <see langword="null"/>.</param>
/// <param name="Port">The port of that broker, or <see langword="null"/> when the endpoint names none.</param>
internal readonly record struct MessagingServerEndpoint(string? Address, int? Port)
{
    // Endpoints come from transport configuration, so a host sees a handful of distinct values. Caching the parse
    // keeps every publish and receive measurement from re-slicing the same string.
    private static readonly ConcurrentDictionary<string, MessagingServerEndpoint> _Cache = new(StringComparer.Ordinal);

    public static MessagingServerEndpoint From(BrokerAddress broker)
    {
        var endpoint = broker.Endpoint;

        return string.IsNullOrEmpty(endpoint) ? default : _Cache.GetOrAdd(endpoint, static e => _Parse(e));
    }

    private static MessagingServerEndpoint _Parse(string endpoint)
    {
        // A multi-broker endpoint (Kafka bootstrap servers, NATS seeds) is reported by its first entry: the attribute
        // holds one server, and the first configured broker is the one an operator recognises.
        var commaIndex = endpoint.IndexOf(',', StringComparison.Ordinal);
        var first = (commaIndex < 0 ? endpoint : endpoint[..commaIndex]).Trim();

        if (first.Length == 0)
        {
            return default;
        }

        if (
            first.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(first, UriKind.Absolute, out var uri)
            && !string.IsNullOrEmpty(uri.Host)
        )
        {
            return new MessagingServerEndpoint(uri.Host, uri.IsDefaultPort || uri.Port < 0 ? null : uri.Port);
        }

        var separatorIndex = first.IndexOf(':', StringComparison.Ordinal);

        if (separatorIndex < 0)
        {
            return new MessagingServerEndpoint(first, Port: null);
        }

        var portSpan = first.AsSpan(separatorIndex + 1);
        var portEnd = portSpan.IndexOf(':');

        if (portEnd >= 0)
        {
            portSpan = portSpan[..portEnd];
        }

        int? port = int.TryParse(portSpan, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

        return new MessagingServerEndpoint(first[..separatorIndex], port);
    }
}
