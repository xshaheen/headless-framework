// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Checks;
using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Messages.Internal;
using Framework.Messages.Processor;
using Framework.Messages.Serialization;
using Framework.Messages.Transport;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for registering and configuring messaging services
/// in a <see cref="IServiceCollection"/> dependency injection container.
/// </summary>
public static class Setup
{
    /// <summary>
    /// Registers and configures all messaging services, consumers, and transport infrastructure.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">A delegate to configure messaging options, storage, transport, and message consumers.</param>
    /// <returns>A <see cref="MessagingBuilder"/> for additional messaging configuration.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configure"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// This method provides a unified API for configuring both messaging infrastructure (storage, transport, retry policies)
    /// and message consumers that implement <see cref="IConsume{TMessage}"/>.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// services.AddMessages(options =>
    /// {
    ///     // Configure infrastructure
    ///     options.FailedRetryCount = 50;
    ///     options.SucceedMessageExpiredAfter = 24 * 3600;
    ///     options.UseSqlServer("connection_string");
    ///     options.UseRabbitMQ(rabbit =>
    ///     {
    ///         rabbit.HostName = "localhost";
    ///         rabbit.Port = 5672;
    ///     });
    ///
    ///     // Configure consumers
    ///     options.ScanConsumers(typeof(Program).Assembly);
    ///
    ///     // Or register specific consumers
    ///     options.Consumer&lt;OrderPlacedHandler&gt;()
    ///         .Topic("orders.placed")
    ///         .Group("order-service")
    ///         .WithConcurrency(5)
    ///         .Build();
    ///
    ///     // Map message types to topics
    ///     options.WithTopicMapping&lt;OrderPlaced&gt;("orders.placed");
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    public static MessagingBuilder AddMessages(this IServiceCollection services, Action<MessagingOptions> configure)
    {
        Argument.IsNotNull(configure);

        var registry = new ConsumerRegistry();
        services.TryAddSingleton<IConsumerRegistry>(registry);
        services.TryAddSingleton(registry);
        var options = new MessagingOptions { Services = services, Registry = registry };

        configure(options);

        return _RegisterCoreMessagingServices(services, options, configure);
    }

    private static MessagingBuilder _RegisterCoreMessagingServices(
        IServiceCollection services,
        MessagingOptions options,
        Action<MessagingOptions>? setupAction
    )
    {
        services.AddSingleton(_ => services);
        services.TryAddSingleton(new MessagingMarkerService("Messages"));
        services.TryAddSingleton<ILongIdGenerator, SnowflakeIdLongIdGenerator>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IOutboxPublisher, OutboxPublisher>();

        services.TryAddSingleton<IConsumerServiceSelector, ConsumerServiceSelector>();
        services.TryAddSingleton<ISubscribeInvoker, SubscribeInvoker>();
        services.TryAddSingleton<MethodMatcherCache>();
        services.TryAddSingleton<IMessageDispatcher, CompiledMessageDispatcher>();

        services.TryAddSingleton<IConsumerRegister, ConsumerRegister>();

        //Processors
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IProcessingServer, IDispatcher>(sp => sp.GetRequiredService<IDispatcher>())
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IProcessingServer, IConsumerRegister>(sp =>
                sp.GetRequiredService<IConsumerRegister>()
            )
        );
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProcessingServer, MessageProcessingServer>());

        //Queue's message processor
        services.TryAddSingleton<MessageNeedToRetryProcessor>();
        services.TryAddSingleton<TransportCheckProcessor>();
        services.TryAddSingleton<MessageDelayedProcessor>();
        services.TryAddSingleton<CollectorProcessor>();

        //Sender
        services.TryAddSingleton<IMessageSender, MessageSender>();

        services.TryAddSingleton<ISerializer, JsonUtf8Serializer>();

        // Warning: IPublishMessageSender need to inject at extension project.
        services.TryAddSingleton<ISubscribeExecutor, SubscribeExecutor>();

        services.TryAddSingleton<IDispatcher, Dispatcher>();

        foreach (var serviceExtension in options.Extensions)
        {
            serviceExtension.AddServices(services);
        }

        if (setupAction is not null)
        {
            services.Configure(setupAction);
        }

        //Startup and Hosted
        services.TryAddSingleton<Bootstrapper>();
        services.TryAddSingleton<IBootstrapper>(sp => sp.GetRequiredService<Bootstrapper>());
        services.AddHostedService(sp => sp.GetRequiredService<Bootstrapper>());

        return new MessagingBuilder(services);
    }
}
