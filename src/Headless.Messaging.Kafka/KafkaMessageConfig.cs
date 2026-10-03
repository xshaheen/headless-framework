// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Headless.Checks;
using Headless.Messaging.Kafka;
using Headless.Messaging.Registration;

namespace Headless.Messaging;

/// <summary>Fluent builder for Kafka publish options applied to a single message type.</summary>
/// <typeparam name="TMessage">The message type being configured.</typeparam>
[PublicAPI]
public sealed class KafkaMessageConfigBuilder<TMessage>
    where TMessage : class
{
    private Func<TMessage, string?>? _partitionSelector;

    /// <summary>
    /// Sets the Kafka message key from the outgoing message payload, which controls partition routing.
    /// Messages with the same key are guaranteed to land on the same partition and are delivered in order.
    /// </summary>
    /// <param name="selector">
    /// A delegate that derives the partition key from the message instance.
    /// Return <see langword="null"/> to use round-robin partition assignment.
    /// Exceptions thrown by the selector are propagated to the caller at publish time.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    public KafkaMessageConfigBuilder<TMessage> PartitionBy(Func<TMessage, string?> selector)
    {
        Argument.IsNotNull(selector);

        _partitionSelector = selector;
        return this;
    }

    internal KafkaMessageConfig<TMessage> Build()
    {
        return new(_partitionSelector);
    }
}

internal sealed class KafkaMessageConfig<TMessage>(Func<TMessage, string?>? partitionSelector)
    : IProviderHeaderContributions
    where TMessage : class
{
    // A message contract merges with an identical redeclaration from another module, so two configs holding the same
    // selector are equal and two different selectors conflict.
    private readonly Func<TMessage, string?>? _selector = partitionSelector;

    public override bool Equals(object? obj) =>
        obj is KafkaMessageConfig<TMessage> other && Equals(_selector, other._selector);

    public override int GetHashCode() => _selector?.GetHashCode() ?? 0;

    public IReadOnlyList<ProviderHeaderContribution> HeaderContributions { get; } =
        partitionSelector is null
            ? []
            :
            [
                new ProviderHeaderContribution(
                    KafkaMessagingHeaders.KafkaKey,
                    message => partitionSelector((TMessage)message)
                ),
            ];
}
