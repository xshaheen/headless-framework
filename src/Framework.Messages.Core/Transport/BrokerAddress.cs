// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages.Transport;

/// <summary>
/// Represents the address information of a message broker, including its name (type) and network endpoint.
/// </summary>
/// <remarks>
/// This struct encapsulates the broker identification and connection information.
/// The <c>ToString()</c> method formats the address as "Name$Endpoint", which can be parsed back using the string constructor.
/// </remarks>
public struct BrokerAddress
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BrokerAddress"/> struct by parsing a combined address string.
    /// </summary>
    /// <remarks>
    /// The address string is expected in the format "BrokerName$Endpoint" where the "$" character acts as a delimiter.
    /// If no "$" is found, the entire string is treated as the endpoint and the name is left empty.
    /// </remarks>
    /// <param name="address">
    /// A combined address string in the format "BrokerName$Endpoint" (e.g., "RabbitMQ$localhost:5672").
    /// </param>
    public BrokerAddress(string address)
    {
        if (address.Contains('$', StringComparison.Ordinal))
        {
            var parts = address.Split('$');

            Name = parts[0];
            Endpoint = string.Concat(parts.Skip(1));
        }
        else
        {
            Name = string.Empty;
            Endpoint = address;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BrokerAddress"/> struct with explicit name and endpoint values.
    /// </summary>
    /// <param name="name">
    /// The broker type name (e.g., "RabbitMQ", "Kafka", "AzureServiceBus").
    /// Can be empty or null for generic addresses.
    /// </param>
    /// <param name="endpoint">
    /// The network endpoint or connection address of the broker (e.g., "localhost:5672", "kafka:9092").
    /// If null, it is treated as an empty string.
    /// </param>
    public BrokerAddress(string name, string? endpoint)
    {
        Name = name;
        Endpoint = endpoint ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the broker type name (e.g., "RabbitMQ", "Kafka", "AzureServiceBus").
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the network endpoint or connection address of the broker.
    /// </summary>
    public string Endpoint { get; set; }

    /// <summary>
    /// Returns the string representation of the broker address in the format "Name$Endpoint".
    /// </summary>
    /// <returns>A string combining the broker name and endpoint separated by "$".</returns>
    public readonly override string ToString() => Name + "$" + Endpoint;
}
