// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Processor;
using Headless.Messaging.Scheduling;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;
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

        // Discover consumers registered via AddConsumer<TConsumer, TMessage>()
        _DiscoverConsumersFromDI(services, registry);

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
        services.TryAddSingleton<IDirectPublisher, DirectPublisher>();

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

        // Register options with values that were set during AddMessages configuration.
        // Don't re-register setupAction as it contains consumer registration logic that
        // requires Services/Registry to be initialized - which only happens in AddMessages.
        services.Configure<MessagingOptions>(opt =>
        {
            // Copy internal state for consumer registration methods
            opt.Services = services;
            opt.Registry = options.Registry;

            // Copy public properties
            opt.DefaultGroupName = options.DefaultGroupName;
            opt.GroupNamePrefix = options.GroupNamePrefix;
            opt.TopicNamePrefix = options.TopicNamePrefix;
            opt.Version = options.Version;
            opt.SucceedMessageExpiredAfter = options.SucceedMessageExpiredAfter;
            opt.FailedMessageExpiredAfter = options.FailedMessageExpiredAfter;
            opt.FailedRetryInterval = options.FailedRetryInterval;
            opt.FailedThresholdCallback = options.FailedThresholdCallback;
            opt.FailedRetryCount = options.FailedRetryCount;
            opt.ConsumerThreadCount = options.ConsumerThreadCount;
            opt.EnableSubscriberParallelExecute = options.EnableSubscriberParallelExecute;
            opt.SubscriberParallelExecuteThreadCount = options.SubscriberParallelExecuteThreadCount;
            opt.SubscriberParallelExecuteBufferFactor = options.SubscriberParallelExecuteBufferFactor;
            opt.EnablePublishParallelSend = options.EnablePublishParallelSend;
            opt.PublishBatchSize = options.PublishBatchSize;
            opt.FallbackWindowLookbackSeconds = options.FallbackWindowLookbackSeconds;
            opt.CollectorCleaningInterval = options.CollectorCleaningInterval;
            opt.SchedulerBatchSize = options.SchedulerBatchSize;
            opt.UseStorageLock = options.UseStorageLock;
            opt.RetryBackoffStrategy = options.RetryBackoffStrategy;

            // Copy internal collections
            foreach (var mapping in options.TopicMappings)
            {
                opt.TopicMappings[mapping.Key] = mapping.Value;
            }
        });

        //Startup and Hosted
        services.TryAddSingleton<Bootstrapper>();
        services.TryAddSingleton<IBootstrapper>(sp => sp.GetRequiredService<Bootstrapper>());
        services.AddHostedService(sp => sp.GetRequiredService<Bootstrapper>());

        // Scheduler registration: only when IScheduledJobStorage is available
        _RegisterSchedulerServices(services, options);

        return new MessagingBuilder(services);
    }

    /// <summary>
    /// Registers scheduler infrastructure services when <see cref="IScheduledJobStorage"/> is available
    /// and scheduled job definitions have been discovered.
    /// </summary>
    private static void _RegisterSchedulerServices(IServiceCollection services, MessagingOptions options)
    {
        var storageDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IScheduledJobStorage));
        var hasStorage = storageDescriptor is not null;
        var hasDefinitions = options.ScheduledJobDefinitions.Count > 0;

        if (!hasStorage && !hasDefinitions)
        {
            return;
        }

        // IScheduledJobStorage is captured by singletons (ScheduledJobManager,
        // SchedulerBackgroundService). A Scoped registration would create a captive
        // dependency — fail fast so the misconfiguration is caught at startup.
        if (storageDescriptor is { Lifetime: ServiceLifetime.Scoped })
        {
            throw new InvalidOperationException(
                "IScheduledJobStorage is registered as Scoped, but it is consumed by singleton services. "
                    + "Register it as Singleton (using a connection-per-call pattern) or Transient."
            );
        }

        // Register the definition registry with all discovered definitions
        var definitionRegistry = new ScheduledJobDefinitionRegistry();

        foreach (var definition in options.ScheduledJobDefinitions)
        {
            definitionRegistry.Add(definition);
        }

        services.TryAddSingleton(definitionRegistry);

        // Core scheduling services
        services.TryAddSingleton<CronScheduleCache>();
        services.TryAddSingleton<IScheduledJobDispatcher, ScheduledJobDispatcher>();
        services.TryAddSingleton<IScheduledJobManager, ScheduledJobManager>();

        // Scheduler options
        services.TryAddSingleton(TimeProvider.System);

        // Background services (only when storage is registered)
        if (hasStorage)
        {
            services.AddHostedService<ScheduledJobReconciler>();
            services.AddHostedService<SchedulerBackgroundService>();
        }
        else if (hasDefinitions)
        {
            services.AddHostedService<SchedulerStorageMissingWarningService>();
        }
    }

    /// <summary>
    /// Discovers and registers consumer metadata instances added via AddConsumer extension method.
    /// </summary>
    private static void _DiscoverConsumersFromDI(IServiceCollection services, ConsumerRegistry registry)
    {
        // Find all ConsumerMetadata instances registered in the service collection
        var metadataDescriptors = services
            .Where(d => d.ServiceType == typeof(ConsumerMetadata) && d.Lifetime == ServiceLifetime.Singleton)
            .ToList();

        foreach (var descriptor in metadataDescriptors)
        {
            if (descriptor.ImplementationInstance is ConsumerMetadata metadata)
            {
                // Skip if already registered (avoid duplicates)
                if (!registry.IsRegistered(metadata.MessageType))
                {
                    registry.Register(metadata);
                }
            }
        }
    }
}
