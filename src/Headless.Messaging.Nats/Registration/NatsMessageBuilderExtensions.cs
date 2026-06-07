// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Nats;

[PublicAPI]
public static class NatsMessageBuilderExtensions
{
    public static IMessageBuilder<TMessage> UseNats<TMessage>(
        this IMessageBuilder<TMessage> builder,
        Action<NatsMessageConfigBuilder<TMessage>> configure
    )
        where TMessage : class
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new NatsMessageConfigBuilder<TMessage>();
        configure(configBuilder);
        ((IMessageProviderConfigBuilder<TMessage>)builder).SetMessageProviderConfig(configBuilder.Build());

        return builder;
    }
}

[PublicAPI]
public sealed class NatsMessageConfigBuilder<TMessage>
    where TMessage : class
{
    private Func<TMessage, string?>? _subjectShardSelector;

    public NatsMessageConfigBuilder<TMessage> SubjectShard(Func<TMessage, string?> selector)
    {
        Argument.IsNotNull(selector);

        _subjectShardSelector = selector;
        return this;
    }

    internal NatsMessageConfig<TMessage> Build() => new(_subjectShardSelector);
}

internal sealed class NatsMessageConfig<TMessage>(Func<TMessage, string?>? subjectShardSelector) : IProviderHeaderContributions
    where TMessage : class
{
    public IReadOnlyList<ProviderHeaderContribution> HeaderContributions { get; } =
            subjectShardSelector is null
                ? []
                :
                [
                    new ProviderHeaderContribution(
                        NatsMessagingHeaders.SubjectShard,
                        message => NatsSubjectShard.Validate(subjectShardSelector((TMessage)message))
                    ),
                ];
}
