// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Headless.Messaging.Transport;

/// <summary>
/// Formats broker endpoint values for <see cref="BrokerAddress" /> and diagnostics surfaces.
/// </summary>
/// <remarks>
/// These helpers are display-only. They keep operator-useful host, port, and path data,
/// but strip credentials so configured secrets are not echoed into logs, traces, or dashboards.
/// </remarks>
public static class BrokerAddressDisplay
{
    private const string _DummyScheme = "mq";

    public static string FormatMany(string? endpoints)
    {
        if (string.IsNullOrWhiteSpace(endpoints))
        {
            return string.Empty;
        }

        return string.Join(
            ",",
            endpoints.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Format)
        );
    }

    public static string Format(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return string.Empty;
        }

        endpoint = endpoint.Trim();

        if (
            !endpoint.Contains("://", StringComparison.Ordinal)
            && endpoint.Contains('@', StringComparison.Ordinal)
            && Uri.TryCreate(_DummyScheme + "://" + endpoint, UriKind.Absolute, out var inferredUri)
        )
        {
            var displayEndpoint = _RemoveCredentials(inferredUri);
            return displayEndpoint.StartsWith(_DummyScheme + "://", StringComparison.Ordinal)
                ? displayEndpoint[(_DummyScheme.Length + 3)..]
                : displayEndpoint;
        }

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var absoluteUri))
        {
            return string.IsNullOrEmpty(absoluteUri.UserInfo) ? endpoint : _RemoveCredentials(absoluteUri);
        }

        return endpoint;
    }

    public static string Format(EndPoint endpoint)
    {
        return endpoint switch
        {
            DnsEndPoint dnsEndPoint => dnsEndPoint.Port > 0
                ? $"{dnsEndPoint.Host}:{dnsEndPoint.Port}"
                : dnsEndPoint.Host,
            IPEndPoint ipEndPoint => $"{ipEndPoint.Address}:{ipEndPoint.Port}",
            _ => endpoint.ToString() ?? string.Empty,
        };
    }

    private static string _RemoveCredentials(Uri uri)
    {
        var builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty };
        var displayEndpoint = builder.Uri.GetLeftPart(UriPartial.Authority);

        if (uri.AbsolutePath is { Length: > 1 })
        {
            displayEndpoint += uri.AbsolutePath;
        }

        return displayEndpoint;
    }
}
