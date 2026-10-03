// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Registration;

namespace Headless.Messaging;

/// <summary>Extension methods that attach RabbitMQ provider-specific options to a tuned consumer.</summary>
[PublicAPI]
public static class RabbitMqMessageBuilderExtensions
{
    /// <summary>
    /// Configures RabbitMQ consumer options for the declared consumer a <c>Tune</c> call targets.
    /// </summary>
    /// <param name="builder">The consumer tuning builder.</param>
    /// <param name="configure">A delegate that configures the RabbitMQ consumer options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ConsumerTuningBuilder UseRabbitMq(
        this ConsumerTuningBuilder builder,
        Action<RabbitMqConsumerConfigBuilder> configure
    )
    {
        _SetConsumerConfig(builder, configure);
        return builder;
    }

    private static void _SetConsumerConfig(
        IConsumerProviderConfigBuilder builder,
        Action<RabbitMqConsumerConfigBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new RabbitMqConsumerConfigBuilder();
        configure(configBuilder);
        builder.SetConsumerProviderConfig(configBuilder.Build());
    }
}
