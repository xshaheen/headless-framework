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
    /// Registers and configures all messaging services in the dependency injection container.
    /// </summary>
    /// <remarks>
    /// This method performs the following registrations:
    /// <list type="bullet">
    /// <item><description>Core services: Message publisher, consumer selector, subscription invoker, and method matcher cache.</description></item>
    /// <item><description>Message processors: Retry processor, transport check processor, delayed message processor, and message collector.</description></item>
    /// <item><description>Message transport: Message sender and default JSON serializer.</description></item>
    /// <item><description>Processing servers: Consumer registration server, dispatcher, and main messaging processing server.</description></item>
    /// <item><description>Bootstrapper and hosted service for application startup and lifecycle management.</description></item>
    /// <item><description>Extensions: Any configured storage and transport extensions (registered via <see cref="MessagingOptions.RegisterExtension"/>).</description></item>
    /// </list>
    /// All core services are registered with singleton lifetime to ensure consistency across the application.
    /// Storage and transport extensions must be added before calling this method (typically through AddCap callback).
    /// </remarks>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> where messaging services will be registered.
    /// This collection represents the application's dependency injection container.
    /// </param>
    /// <param name="setupAction">
    /// A delegate that configures the <see cref="MessagingOptions"/> settings for the messaging system.
    /// This action is invoked to customize behavior such as message expiration, retry policies, concurrency settings,
    /// and to register storage and transport extensions.
    /// Use this to call <c>UseRabbitMQ()</c>, <c>UseSqlServer()</c>, and other extension methods.
    /// </param>
    /// <returns>
    /// A <see cref="MessagingBuilder"/> instance that provides a fluent API for additional messaging configuration,
    /// such as registering subscriber filters and custom subscriber assembly scanning.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="setupAction"/> is null.
    /// </exception>
    /// <example>
    /// <code>
    /// services.AddCap(options =>
    /// {
    ///     // Configure options
    ///     options.SucceedMessageExpiredAfter = 24 * 3600;
    ///     options.FailedRetryCount = 50;
    ///
    ///     // Register storage backend
    ///     options.UseSqlServer("your_connection_string");
    ///
    ///     // Register message transport
    ///     options.UseRabbitMQ(rabbitMqOptions =>
    ///     {
    ///         rabbitMqOptions.HostName = "localhost";
    ///         rabbitMqOptions.Port = 5672;
    ///     });
    /// })
    /// .AddSubscribeFilter&lt;LoggingFilter&gt;()
    /// .AddSubscriberAssembly(typeof(MyCapHandlers));
    /// </code>
    /// </example>
    [Obsolete("AddCap is deprecated. Use AddMessages() for the new IConsume<T> pattern, or continue using AddCap with the new MessagingOptions parameter name.", false)]
    public static MessagingBuilder AddCap(this IServiceCollection services, Action<MessagingOptions> setupAction)
    {
        Argument.IsNotNull(setupAction);

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

        //Options and extension service
        var options = new MessagingOptions();
        setupAction(options);

        services.TryAddSingleton<IDispatcher, Dispatcher>();

        foreach (var serviceExtension in options.Extensions)
        {
            serviceExtension.AddServices(services);
        }

        services.Configure(setupAction);

        //Startup and Hosted
        services.AddSingleton<Bootstrapper>();
        services.AddHostedService(sp => sp.GetRequiredService<Bootstrapper>());
        services.AddSingleton<IBootstrapper>(sp => sp.GetRequiredService<Bootstrapper>());

        return new MessagingBuilder(services);
    }

    /// <summary>
    /// Registers message consumers and configures messaging infrastructure using the type-safe IConsume&lt;T&gt; pattern.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">A delegate to configure message consumers and topic mappings.</param>
    /// <returns>A <see cref="MessagingBuilder"/> for additional messaging configuration.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="configure"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// This method provides a fluent API for registering message consumers that implement <see cref="IConsume{TMessage}"/>.
    /// It supports both automatic assembly scanning and explicit consumer registration.
    /// </para>
    /// <para>
    /// <strong>Example:</strong>
    /// <code>
    /// services.AddMessages(messaging =>
    /// {
    ///     // Auto-discover consumers
    ///     messaging.ScanConsumers(typeof(Program).Assembly);
    ///
    ///     // Or register specific consumers
    ///     messaging.Consumer&lt;OrderPlacedHandler&gt;()
    ///         .Topic("orders.placed")
    ///         .Group("order-service")
    ///         .WithConcurrency(5)
    ///         .Build();
    ///
    ///     // Map message types to topics
    ///     messaging.WithTopicMapping&lt;OrderPlaced&gt;("orders.placed");
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    public static MessagingBuilder AddMessages(this IServiceCollection services, Action<IMessagingBuilder> configure)
    {
        Argument.IsNotNull(configure);

        // Create the consumer configurator
        var builder = new ConsumerConfigurator(services);

        // Let the user configure consumers
        configure(builder);

        // Continue with standard messaging registration (empty config - user configures messaging separately)
        return services.AddCap(_ => { });
    }
}
