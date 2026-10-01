// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Aws;
using Headless.Messaging.Registration;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>Extension methods that attach AWS SQS provider-specific options to a message contract.</summary>
[PublicAPI]
public static class AwsMessageBuilderExtensions
{
    /// <summary>
    /// Configures AWS SQS options for <typeparamref name="TMessage"/> publish operations.
    /// </summary>
    /// <typeparam name="TMessage">The message type being registered.</typeparam>
    /// <param name="builder">The Bus route of the message contract.</param>
    /// <param name="configure">A delegate that configures the AWS SQS options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IBusContractBuilder<TMessage> UseAws<TMessage>(
        this IBusContractBuilder<TMessage> builder,
        Action<AwsMessageConfigBuilder<TMessage>> configure
    )
        where TMessage : class
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new AwsMessageConfigBuilder<TMessage>();
        configure(configBuilder);
        ((IMessageProviderConfigBuilder<TMessage>)builder).SetMessageProviderConfig(configBuilder.Build());

        return builder;
    }

    /// <summary>Configures AWS SQS options for <typeparamref name="TMessage"/> queue publish operations.</summary>
    public static IQueueContractBuilder<TMessage> UseAws<TMessage>(
        this IQueueContractBuilder<TMessage> builder,
        Action<AwsMessageConfigBuilder<TMessage>> configure
    )
        where TMessage : class
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new AwsMessageConfigBuilder<TMessage>();
        configure(configBuilder);
        ((IMessageProviderConfigBuilder<TMessage>)builder).SetMessageProviderConfig(configBuilder.Build());

        return builder;
    }
}

/// <summary>Fluent builder for AWS SQS publish options applied to a single message type.</summary>
/// <typeparam name="TMessage">The message type being configured.</typeparam>
[PublicAPI]
public sealed class AwsMessageConfigBuilder<TMessage>
    where TMessage : class
{
    private Func<TMessage, string?>? _messageGroupIdSelector;

    /// <summary>
    /// Sets the AWS SQS FIFO <c>MessageGroupId</c> from the outgoing message payload.
    /// Messages with the same group identifier are delivered in order within the group.
    /// </summary>
    /// <param name="selector">
    /// A delegate that derives the message group identifier from the message instance.
    /// Must return a value that is 128 characters or fewer; longer values throw
    /// <see cref="InvalidOperationException"/> at publish time. Return <see langword="null"/> to omit
    /// the group identifier (only valid for non-FIFO queues).
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    public AwsMessageConfigBuilder<TMessage> MessageGroupId(Func<TMessage, string?> selector)
    {
        Argument.IsNotNull(selector);

        _messageGroupIdSelector = selector;
        return this;
    }

    internal AwsMessageConfig<TMessage> Build()
    {
        return new(_messageGroupIdSelector);
    }
}

internal sealed class AwsMessageConfig<TMessage>(Func<TMessage, string?>? messageGroupIdSelector)
    : IProviderHeaderContributions
    where TMessage : class
{
    // A message contract merges with an identical redeclaration from another module, so two configs holding the same
    // selector are equal and two different selectors conflict.
    private readonly Func<TMessage, string?>? _selector = messageGroupIdSelector;

    public override bool Equals(object? obj) =>
        obj is AwsMessageConfig<TMessage> other && Equals(_selector, other._selector);

    public override int GetHashCode() => _selector?.GetHashCode() ?? 0;

    private const int _MessageGroupIdMaxLength = 128;

    public IReadOnlyList<ProviderHeaderContribution> HeaderContributions { get; } =
        messageGroupIdSelector is null
            ? []
            :
            [
                new ProviderHeaderContribution(
                    AwsMessagingHeaders.MessageGroupId,
                    message => _ValidateMessageGroupId(messageGroupIdSelector((TMessage)message))
                ),
            ];

    private static string? _ValidateMessageGroupId(string? messageGroupId)
    {
        if (messageGroupId is null || messageGroupId.Length <= _MessageGroupIdMaxLength)
        {
            return messageGroupId;
        }

        throw new InvalidOperationException(
            $"AWS SQS MessageGroupId must be {_MessageGroupIdMaxLength} characters or fewer."
        );
    }
}
