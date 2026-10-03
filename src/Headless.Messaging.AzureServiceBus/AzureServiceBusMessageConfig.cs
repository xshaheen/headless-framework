// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Registration;

namespace Headless.Messaging;

/// <summary>Fluent builder for Azure Service Bus publish options applied to a single message type.</summary>
/// <typeparam name="TMessage">The message type being configured.</typeparam>
[PublicAPI]
public sealed class AzureServiceBusMessageConfigBuilder<TMessage>
    where TMessage : class
{
    private Func<TMessage, string?>? _partitionKeySelector;

    /// <summary>
    /// Sets the Azure Service Bus <c>PartitionKey</c> from the outgoing message payload.
    /// The broker uses this value to guarantee ordering within a partition.
    /// </summary>
    /// <param name="selector">
    /// A delegate that derives the partition key from the message instance.
    /// Must return a value that is 128 characters or fewer; longer values throw
    /// <see cref="InvalidOperationException"/> at publish time. Return <see langword="null"/> to omit
    /// the partition key and let the broker assign the message to any partition.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    public AzureServiceBusMessageConfigBuilder<TMessage> PartitionKey(Func<TMessage, string?> selector)
    {
        Argument.IsNotNull(selector);

        _partitionKeySelector = selector;
        return this;
    }

    internal AzureServiceBusMessageConfig<TMessage> Build()
    {
        return new(_partitionKeySelector);
    }
}

internal sealed class AzureServiceBusMessageConfig<TMessage>(Func<TMessage, string?>? partitionKeySelector)
    : IProviderHeaderContributions
    where TMessage : class
{
    // A message contract merges with an identical redeclaration from another module, so two configs holding the same
    // selector are equal and two different selectors conflict.
    private readonly Func<TMessage, string?>? _selector = partitionKeySelector;

    public override bool Equals(object? obj) =>
        obj is AzureServiceBusMessageConfig<TMessage> other && Equals(_selector, other._selector);

    public override int GetHashCode() => _selector?.GetHashCode() ?? 0;

    private const int _PartitionKeyMaxLength = 128;

    public IReadOnlyList<ProviderHeaderContribution> HeaderContributions { get; } =
        partitionKeySelector is null
            ? []
            :
            [
                new ProviderHeaderContribution(
                    AzureServiceBusMessagingHeaders.PartitionKey,
                    message => _ValidatePartitionKey(partitionKeySelector((TMessage)message))
                ),
            ];

    private static string? _ValidatePartitionKey(string? partitionKey)
    {
        if (partitionKey is null || partitionKey.Length <= _PartitionKeyMaxLength)
        {
            return partitionKey;
        }

        throw new InvalidOperationException(
            $"Azure Service Bus PartitionKey must be {_PartitionKeyMaxLength} characters or fewer."
        );
    }
}
