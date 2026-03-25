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
    public static string GetDisplayEndpoints(string? endpoints, string? inferredScheme = null)
    {
        if (string.IsNullOrWhiteSpace(endpoints))
        {
            return string.Empty;
        }

        return string.Join(
            ",",
            endpoints
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(endpoint => GetDisplayEndpoint(endpoint, inferredScheme))
        );
    }

    public static string GetDisplayEndpoint(string? endpoint, string? inferredScheme = null)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return string.Empty;
        }

        endpoint = endpoint.Trim();

        if (
            !string.IsNullOrWhiteSpace(inferredScheme)
            && !endpoint.Contains("://", StringComparison.Ordinal)
            && endpoint.Contains('@')
            && Uri.TryCreate(inferredScheme + "://" + endpoint, UriKind.Absolute, out var inferredUri)
        )
        {
            return inferredUri.IsDefaultPort ? inferredUri.Host : $"{inferredUri.Host}:{inferredUri.Port}";
        }

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var absoluteUri))
        {
            return string.IsNullOrEmpty(absoluteUri.UserInfo) ? endpoint : _RemoveCredentials(absoluteUri);
        }

        return endpoint;
    }

    public static string GetDisplayEndpoint(EndPoint endpoint)
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

        if (uri.PathAndQuery is { Length: > 1 })
        {
            displayEndpoint += uri.PathAndQuery;
        }

        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            displayEndpoint += uri.Fragment;
        }

        return displayEndpoint;
    }
}
