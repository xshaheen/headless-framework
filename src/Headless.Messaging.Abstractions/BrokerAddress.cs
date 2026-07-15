// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Represents the address information of a message broker, including its name (type) and network endpoint.
/// </summary>
/// <remarks>
/// This struct encapsulates the broker identification and connection information.
/// The <c>ToString()</c> method formats the address as "Name$Endpoint", which can be parsed back using the string constructor.
/// </remarks>
[PublicAPI]
public readonly struct BrokerAddress : IEquatable<BrokerAddress>
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
        var separatorIndex = address.IndexOf('$', StringComparison.Ordinal);

        if (separatorIndex >= 0)
        {
            Name = address[..separatorIndex];
            Endpoint = address[(separatorIndex + 1)..];
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
    /// Can be empty or null for generic addresses. Must not contain the '$' character — it is the
    /// reserved delimiter for the single-string form used by <see cref="ToString"/> and the
    /// string-only constructor.
    /// </param>
    /// <param name="endpoint">
    /// The network endpoint or connection address of the broker (e.g., "localhost:5672", "kafka:9092").
    /// If null, it is treated as an empty string.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> contains the reserved '$' separator.
    /// </exception>
    public BrokerAddress(string? name, string? endpoint)
    {
        if (name?.Contains('$', StringComparison.Ordinal) == true)
        {
            throw new ArgumentException(
                "BrokerAddress.Name must not contain the reserved '$' separator.",
                nameof(name)
            );
        }

        Name = name ?? string.Empty;
        Endpoint = endpoint ?? string.Empty;
    }

    /// <summary>
    /// Gets the broker type name (e.g., "RabbitMQ", "Kafka", "AzureServiceBus").
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Gets the network endpoint or connection address of the broker.
    /// </summary>
    public string Endpoint { get; init; }

    /// <summary>
    /// Returns the string representation of the broker address in the format "Name$Endpoint".
    /// </summary>
    /// <returns>A string combining the broker name and endpoint separated by "$".</returns>
    public override readonly string ToString()
    {
        return Name + "$" + Endpoint;
    }

    public readonly bool Equals(BrokerAddress other)
    {
        return string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(Endpoint, other.Endpoint, StringComparison.Ordinal);
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is BrokerAddress other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(Name, Endpoint);
    }

    public static bool operator ==(BrokerAddress left, BrokerAddress right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(BrokerAddress left, BrokerAddress right)
    {
        return !(left == right);
    }
}
