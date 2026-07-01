// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Registration;

namespace Headless.Messaging.Aws;

/// <summary>Extension methods that attach AWS SQS provider-specific options to a message registration.</summary>
[PublicAPI]
public static class AwsMessageBuilderExtensions
{
    /// <summary>
    /// Configures AWS SQS options for <typeparamref name="TMessage"/> publish operations.
    /// </summary>
    /// <typeparam name="TMessage">The message type being registered.</typeparam>
    /// <param name="builder">The message builder.</param>
    /// <param name="configure">A delegate that configures the AWS SQS options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// Requires the framework-provided <see cref="IMessageBuilder{TMessage}"/> from
    /// <c>setup.ForMessage&lt;TMessage&gt;</c>; custom or mocked builder implementations are not supported.
    /// </remarks>
    public static IMessageBuilder<TMessage> UseAws<TMessage>(
        this IMessageBuilder<TMessage> builder,
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

    internal AwsMessageConfig<TMessage> Build() => new(_messageGroupIdSelector);
}

internal sealed class AwsMessageConfig<TMessage>(Func<TMessage, string?>? messageGroupIdSelector)
    : IProviderHeaderContributions
    where TMessage : class
{
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
