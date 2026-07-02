// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Registration;

namespace Headless.Messaging.AzureServiceBus;

/// <summary>Extension methods that attach Azure Service Bus provider-specific options to a message registration.</summary>
[PublicAPI]
public static class AzureServiceBusMessageBuilderExtensions
{
    /// <summary>
    /// Configures Azure Service Bus options for <typeparamref name="TMessage"/> publish operations.
    /// </summary>
    /// <typeparam name="TMessage">The message type being registered.</typeparam>
    /// <param name="builder">The message builder.</param>
    /// <param name="configure">A delegate that configures the Azure Service Bus options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// Requires the framework-provided <see cref="IMessageBuilder{TMessage}"/> from
    /// <c>setup.ForMessage&lt;TMessage&gt;</c>; custom or mocked builder implementations are not supported.
    /// </remarks>
    public static IMessageBuilder<TMessage> UseAzureServiceBus<TMessage>(
        this IMessageBuilder<TMessage> builder,
        Action<AzureServiceBusMessageConfigBuilder<TMessage>> configure
    )
        where TMessage : class
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new AzureServiceBusMessageConfigBuilder<TMessage>();
        configure(configBuilder);
        ((IMessageProviderConfigBuilder<TMessage>)builder).SetMessageProviderConfig(configBuilder.Build());

        return builder;
    }
}

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

    internal AzureServiceBusMessageConfig<TMessage> Build() => new(_partitionKeySelector);
}

internal sealed class AzureServiceBusMessageConfig<TMessage>(Func<TMessage, string?>? partitionKeySelector)
    : IProviderHeaderContributions
    where TMessage : class
{
    private const int _PartitionKeyMaxLength = 128;

    public IReadOnlyList<ProviderHeaderContribution> HeaderContributions { get; } =
        partitionKeySelector is null
            ? []
            :
            [
                new ProviderHeaderContribution(
                    AzureServiceBusHeaders.PartitionKey,
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
