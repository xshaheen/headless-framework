// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Aws;

[PublicAPI]
public static class AwsMessageBuilderExtensions
{
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

[PublicAPI]
public sealed class AwsMessageConfigBuilder<TMessage>
    where TMessage : class
{
    private Func<TMessage, string?>? _messageGroupIdSelector;

    public AwsMessageConfigBuilder<TMessage> MessageGroupId(Func<TMessage, string?> selector)
    {
        Argument.IsNotNull(selector);

        _messageGroupIdSelector = selector;
        return this;
    }

    internal AwsMessageConfig<TMessage> Build() => new(_messageGroupIdSelector);
}

internal sealed class AwsMessageConfig<TMessage>(Func<TMessage, string?>? messageGroupIdSelector) : IProviderHeaderContributions
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
