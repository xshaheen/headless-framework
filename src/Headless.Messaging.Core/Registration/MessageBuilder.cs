// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging.Registration;

/// <summary>
/// Configures message-level metadata and consumer registrations for a message type.
/// </summary>
/// <typeparam name="TMessage">The message type being registered.</typeparam>
[PublicAPI]
public interface IMessageBuilder<TMessage>
    where TMessage : class
{
    /// <summary>Overrides the convention-derived message name for publishing and consuming this message type.</summary>
    IMessageBuilder<TMessage> MessageName(string messageName);

    /// <summary>
    /// Derives the correlation identifier from the outgoing message payload when no explicit correlation is supplied.
    /// </summary>
    /// <remarks>
    /// Correlation resolution order (first non-empty value wins):
    /// <list type="number">
    ///   <item><description>Explicit value from <see cref="MessageOptions.CorrelationId"/>.</description></item>
    ///   <item><description>Value returned by this <paramref name="selector"/>.</description></item>
    ///   <item><description>Ambient correlation from the active <see cref="ConsumeContext{TMessage}"/> (propagation).</description></item>
    ///   <item><description>The outgoing message identifier (fallback).</description></item>
    /// </list>
    /// Exceptions thrown by the selector are wrapped in <see cref="InvalidOperationException"/>
    /// and include the message type name to aid diagnostics.
    /// <para>
    /// <strong>Broker length limits:</strong> some brokers cap the correlation-id field (for example,
    /// RabbitMQ and NATS cap it at approximately 255 characters). The framework does <em>not</em>
    /// truncate or validate the value returned by <paramref name="selector"/>; callers are responsible
    /// for ensuring the value fits within their broker's limit.
    /// </para>
    /// </remarks>
    /// <param name="selector">A delegate that extracts the correlation identifier from the message payload.</param>
    IMessageBuilder<TMessage> CorrelationFrom(Func<TMessage, string?> selector);

    /// <summary>Registers a broadcast bus consumer for this message type.</summary>
    IMessageBuilder<TMessage> OnBus<TConsumer>()
        where TConsumer : class, IConsume<TMessage>;

    /// <summary>Registers and configures a broadcast bus consumer for this message type.</summary>
    IMessageBuilder<TMessage> OnBus<TConsumer>(Action<IBusConsumerBuilder<TConsumer>> configure)
        where TConsumer : class, IConsume<TMessage>;

    /// <summary>Registers a point-to-point queue consumer for this message type.</summary>
    IMessageBuilder<TMessage> OnQueue<TConsumer>()
        where TConsumer : class, IConsume<TMessage>;

    /// <summary>Registers and configures a point-to-point queue consumer for this message type.</summary>
    IMessageBuilder<TMessage> OnQueue<TConsumer>(Action<IQueueConsumerBuilder<TConsumer>> configure)
        where TConsumer : class, IConsume<TMessage>;
}

internal sealed class MessageBuilder<TMessage>(IServiceCollection services)
    : IMessageBuilder<TMessage>,
        IMessageProviderConfigBuilder<TMessage>
    where TMessage : class
{
    private readonly List<MessageConsumerRegistrationBuilder> _consumers = [];
    private readonly ProviderConfigBag _providerConfigs = new();
    private string? _messageName;
    private Func<object, string?>? _correlationSelector;

    public IMessageBuilder<TMessage> MessageName(string messageName)
    {
        Argument.IsNotNullOrWhiteSpace(messageName);

        _messageName = messageName;
        return this;
    }

    public IMessageBuilder<TMessage> CorrelationFrom(Func<TMessage, string?> selector)
    {
        Argument.IsNotNull(selector);

        _correlationSelector = message => selector((TMessage)message);
        return this;
    }

    public IMessageBuilder<TMessage> OnBus<TConsumer>()
        where TConsumer : class, IConsume<TMessage>
    {
        return OnBus<TConsumer>(static _ => { });
    }

    public IMessageBuilder<TMessage> OnBus<TConsumer>(Action<IBusConsumerBuilder<TConsumer>> configure)
        where TConsumer : class, IConsume<TMessage>
    {
        Argument.IsNotNull(configure);

        var registration = _AddConsumer<TConsumer>(IntentType.Bus);
        configure(new BusConsumerBuilder<TConsumer>(registration));

        return this;
    }

    public IMessageBuilder<TMessage> OnQueue<TConsumer>()
        where TConsumer : class, IConsume<TMessage>
    {
        return OnQueue<TConsumer>(static _ => { });
    }

    public IMessageBuilder<TMessage> OnQueue<TConsumer>(Action<IQueueConsumerBuilder<TConsumer>> configure)
        where TConsumer : class, IConsume<TMessage>
    {
        Argument.IsNotNull(configure);

        var registration = _AddConsumer<TConsumer>(IntentType.Queue);
        configure(new QueueConsumerBuilder<TConsumer>(registration));

        return this;
    }

    internal MessageRegistration Build()
    {
        var providerConfigs = _providerConfigs.Build();

        return new MessageRegistration(
            typeof(TMessage),
            _messageName,
            _correlationSelector,
            providerConfigs,
            _consumers.ConvertAll(x => x.Build(providerConfigs))
        );
    }

    void IMessageProviderConfigBuilder<TMessage>.SetMessageProviderConfig(object config) =>
        _providerConfigs.Set(config);

    private MessageConsumerRegistrationBuilder _AddConsumer<TConsumer>(IntentType intentType)
        where TConsumer : class, IConsume<TMessage>
    {
        services.TryAddScoped<TConsumer>();
        services.TryAddScoped<IConsume<TMessage>>(sp => sp.GetRequiredService<TConsumer>());

        var registration = new MessageConsumerRegistrationBuilder(typeof(TConsumer), intentType);
        _consumers.Add(registration);

        return registration;
    }
}
