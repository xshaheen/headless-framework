// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.AzureServiceBus;

[PublicAPI]
public static class AzureServiceBusMessageBuilderExtensions
{
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

[PublicAPI]
public sealed class AzureServiceBusMessageConfigBuilder<TMessage>
    where TMessage : class
{
    private Func<TMessage, string?>? _partitionKeySelector;

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

    public IReadOnlyList<ProviderHeaderContribution> HeaderContributions =>
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
