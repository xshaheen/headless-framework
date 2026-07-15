// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;

namespace Headless.Messaging.AzureServiceBus.Helpers;

/// <summary>Utility helpers for Azure Service Bus connection and address resolution.</summary>
public static class ServiceBusHelpers
{
    /// <summary>
    /// Creates a <see cref="ServiceBusClient"/> from the configured authentication mode: token
    /// credential + namespace when <see cref="AzureServiceBusMessagingOptions.TokenCredential"/> is
    /// set, otherwise the SAS connection string.
    /// </summary>
    /// <param name="options">The messaging options carrying the authentication configuration.</param>
    public static ServiceBusClient CreateClient(AzureServiceBusMessagingOptions options)
    {
        return options.TokenCredential is null
            ? new ServiceBusClient(options.ConnectionString)
            : new ServiceBusClient(options.Namespace, options.TokenCredential);
    }

    /// <summary>
    /// Resolves the broker address from either a Service Bus connection string or a namespace hostname.
    /// </summary>
    /// <param name="connectionString">A Service Bus SAS connection string, or <see langword="null"/>.</param>
    /// <param name="namespace">
    /// The fully-qualified Service Bus namespace hostname, or <see langword="null"/>.
    /// When both are supplied, <paramref name="namespace"/> takes precedence.
    /// </param>
    /// <returns>A <c>BrokerAddress</c> suitable for telemetry and health checks.</returns>
    /// <exception cref="ArgumentException">Both parameters are null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The namespace cannot be extracted from the connection string.</exception>
    public static BrokerAddress GetBrokerAddress(string? connectionString, string? @namespace)
    {
        var host = (@namespace, connectionString) switch
        {
            _ when string.IsNullOrWhiteSpace(@namespace) && string.IsNullOrWhiteSpace(connectionString) =>
                throw new ArgumentException(
                    "Either connection string or namespace are required.",
                    nameof(connectionString)
                ),
            _ when string.IsNullOrWhiteSpace(connectionString)
                    || (!string.IsNullOrWhiteSpace(@namespace) && !string.IsNullOrWhiteSpace(connectionString)) =>
                @namespace,
            _ when string.IsNullOrWhiteSpace(@namespace) => _TryGetEndpointFromConnectionString(
                connectionString,
                out var extractedValue
            )
                ? extractedValue
                : throw new InvalidOperationException("Unable to extract namespace from connection string."),
            _ => throw new InvalidOperationException("Unhandled case in switch expression."),
        };

        return new BrokerAddress("servicebus", host);
    }

    private static bool _TryGetEndpointFromConnectionString(string? connectionString, out string? @namespace)
    {
        @namespace = string.Empty;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var keyValuePairs = connectionString.Split(';');

        foreach (var kvp in keyValuePairs)
        {
            if (!kvp.StartsWith("Endpoint", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var endpointParts = kvp.Split('=');

            if (endpointParts.Length != 2)
            {
                continue;
            }

            var uri = new Uri(endpointParts[1]);

            // Namespace is the host part without the .servicebus.windows.net
            @namespace = uri.ToString();

            return true;
        }

        return false;
    }
}
