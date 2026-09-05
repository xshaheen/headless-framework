// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging.Registration;

/// <summary>Configures message metadata and broadcast bus consumers for a message type.</summary>
/// <typeparam name="TMessage">The message type being registered.</typeparam>
[PublicAPI]
public interface IBusMessageBuilder<TMessage>
    where TMessage : class
{
    /// <summary>Defines the stable logical name and schema version of this message contract.</summary>
    IBusMessageBuilder<TMessage> Contract(string name, string version = MessageOptions.InitialContractVersion);

    /// <summary>Derives a correlation identifier from the outgoing payload.</summary>
    IBusMessageBuilder<TMessage> CorrelationFrom(Func<TMessage, string?> selector);

    /// <summary>Requires a locally supported native affinity mapping for this route at startup.</summary>
    IBusMessageBuilder<TMessage> RequireRoutingAffinity();

    /// <summary>Registers and configures a Bus consumer.</summary>
    IBusMessageBuilder<TMessage> Consumer<TConsumer>(Action<IBusConsumerBuilder<TConsumer>> configure)
        where TConsumer : class, IConsume<TMessage>;
}

/// <summary>Configures message metadata and point-to-point Queue consumers for a message type.</summary>
/// <typeparam name="TMessage">The message type being registered.</typeparam>
[PublicAPI]
public interface IQueueMessageBuilder<TMessage>
    where TMessage : class
{
    /// <summary>Defines the stable logical name and schema version of this message contract.</summary>
    IQueueMessageBuilder<TMessage> Contract(string name, string version = MessageOptions.InitialContractVersion);

    /// <summary>Derives a correlation identifier from the outgoing payload.</summary>
    IQueueMessageBuilder<TMessage> CorrelationFrom(Func<TMessage, string?> selector);

    /// <summary>Requires a locally supported native affinity mapping for this route at startup.</summary>
    IQueueMessageBuilder<TMessage> RequireRoutingAffinity();

    /// <summary>Registers and configures a Queue consumer.</summary>
    IQueueMessageBuilder<TMessage> Consumer<TConsumer>(Action<IQueueConsumerBuilder<TConsumer>> configure)
        where TConsumer : class, IConsume<TMessage>;
}

internal abstract class MessageBuilder<TMessage>(IServiceCollection services, MessageLane lane)
    : IMessageProviderConfigBuilder<TMessage>
    where TMessage : class
{
    private readonly List<MessageConsumerRegistrationBuilder> _consumers = [];
    private readonly ProviderConfigBag _providerConfigs = new();
    private string? _messageName;
    private string _contractVersion = MessageOptions.InitialContractVersion;
    private Func<object, string?>? _correlationSelector;
    private bool _requiresRoutingAffinity;

    protected void SetRoutingAffinityRequired() => _requiresRoutingAffinity = true;

    protected MessageRegistration BuildRegistration()
    {
        var providerConfigs = _providerConfigs.Build();

        return new MessageRegistration(
            typeof(TMessage),
            lane,
            _messageName,
            _correlationSelector,
            providerConfigs,
            _consumers.ConvertAll(x => x.Build(providerConfigs)),
            _contractVersion,
            _requiresRoutingAffinity
        );
    }

    protected void SetContract(string name, string version)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        MessagingOptions.ValidateContractVersion(version);
        _messageName = name;
        _contractVersion = version;
    }

    protected void SetCorrelationFrom(Func<TMessage, string?> selector)
    {
        Argument.IsNotNull(selector);
        _correlationSelector = message => selector((TMessage)message);
    }

    protected MessageConsumerRegistrationBuilder AddConsumer<TConsumer>()
        where TConsumer : class, IConsume<TMessage>
    {
        services.TryAddScoped<TConsumer>();
        services.TryAddScoped<IConsume<TMessage>>(sp => sp.GetRequiredService<TConsumer>());

        var registration = new MessageConsumerRegistrationBuilder(typeof(TConsumer), lane);
        _consumers.Add(registration);
        return registration;
    }

    void IMessageProviderConfigBuilder<TMessage>.SetMessageProviderConfig(object config) =>
        _providerConfigs.Set(config);
}

internal sealed class BusMessageBuilder<TMessage>(IServiceCollection services)
    : MessageBuilder<TMessage>(services, MessageLane.Bus),
        IBusMessageBuilder<TMessage>
    where TMessage : class
{
    public IBusMessageBuilder<TMessage> Contract(string name, string version = MessageOptions.InitialContractVersion)
    {
        SetContract(name, version);
        return this;
    }

    public IBusMessageBuilder<TMessage> RequireRoutingAffinity()
    {
        SetRoutingAffinityRequired();
        return this;
    }

    public IBusMessageBuilder<TMessage> CorrelationFrom(Func<TMessage, string?> selector)
    {
        SetCorrelationFrom(selector);
        return this;
    }

    public IBusMessageBuilder<TMessage> Consumer<TConsumer>(Action<IBusConsumerBuilder<TConsumer>> configure)
        where TConsumer : class, IConsume<TMessage>
    {
        Argument.IsNotNull(configure);
        configure(new BusConsumerBuilder<TConsumer>(AddConsumer<TConsumer>()));
        return this;
    }

    internal MessageRegistration Build() => BuildRegistration();
}

internal sealed class QueueMessageBuilder<TMessage>(IServiceCollection services)
    : MessageBuilder<TMessage>(services, MessageLane.Queue),
        IQueueMessageBuilder<TMessage>
    where TMessage : class
{
    public IQueueMessageBuilder<TMessage> Contract(string name, string version = MessageOptions.InitialContractVersion)
    {
        SetContract(name, version);
        return this;
    }

    public IQueueMessageBuilder<TMessage> RequireRoutingAffinity()
    {
        SetRoutingAffinityRequired();
        return this;
    }

    public IQueueMessageBuilder<TMessage> CorrelationFrom(Func<TMessage, string?> selector)
    {
        SetCorrelationFrom(selector);
        return this;
    }

    public IQueueMessageBuilder<TMessage> Consumer<TConsumer>(Action<IQueueConsumerBuilder<TConsumer>> configure)
        where TConsumer : class, IConsume<TMessage>
    {
        Argument.IsNotNull(configure);
        configure(new QueueConsumerBuilder<TConsumer>(AddConsumer<TConsumer>()));
        return this;
    }

    internal MessageRegistration Build() => BuildRegistration();
}
