// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging;

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

internal sealed class MessageBuilder<TMessage>(IServiceCollection services) : IMessageBuilder<TMessage>
    where TMessage : class
{
    private readonly List<MessageConsumerRegistrationBuilder> _consumers = [];
    private string? _messageName;

    public IMessageBuilder<TMessage> MessageName(string messageName)
    {
        Argument.IsNotNullOrWhiteSpace(messageName);

        _messageName = messageName;
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
        return new MessageRegistration(
            typeof(TMessage),
            _messageName,
            _consumers.Select(static x => x.Build()).ToList()
        );
    }

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
