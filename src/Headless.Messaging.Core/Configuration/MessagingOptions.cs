// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using FluentValidation;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.CircuitBreaker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging.Configuration;

/// <summary>
/// Provides options to customize various aspects of the message processing pipeline. This includes settings for message expiration,
/// retry mechanisms, concurrency management, and serialization, among others. This class allows fine-tuning
/// messaging behavior to better align with specific application requirements, such as adjusting threading models for
/// subscriber message processing, setting message expiry times, and customizing serialization settings.
/// </summary>
public class MessagingOptions : IMessagingBuilder
{
#pragma warning disable IDE0032
    private string _defaultGroupName =
        "headless.queue." + Assembly.GetEntryAssembly()?.GetName().Name!.ToLower(CultureInfo.InvariantCulture);
#pragma warning restore IDE0032

    internal IServiceCollection? Services { get; set; }
    internal ConsumerRegistry? Registry { get; set; }
    internal ConsumerCircuitBreakerRegistry CircuitBreakerRegistry { get; } = new();
    internal Dictionary<Type, string> TopicMappings { get; } = new();
    internal IList<IMessagesOptionsExtension> Extensions { get; } = new List<IMessagesOptionsExtension>();
    internal MessagingConventions Conventions { get; set; } = new();

    /// <summary>
    /// Gets or sets the default consumer group name for subscribers.
    /// In Kafka, this corresponds to the consumer group name; in RabbitMQ, it corresponds to the queue name.
    /// Default value is "headless.queue." followed by the entry assembly name in lowercase.
    /// </summary>
    public string DefaultGroupName
    {
        get => _defaultGroupName;
        set
        {
            _defaultGroupName = value;
            IsDefaultGroupNameConfigured = true;
        }
    }

    internal bool IsDefaultGroupNameConfigured { get; set; }

    /// <summary>
    /// Gets or sets an optional prefix to be prepended to all consumer group names.
    /// </summary>
    public string? GroupNamePrefix { get; set; }

    /// <summary>
    /// Gets or sets an optional prefix to be prepended to all topic names.
    /// </summary>
    public string? TopicNamePrefix { get; set; }

    /// <summary>
    /// Gets or sets the version identifier for messages, used to isolate data between different instances or deployments.
    /// This allows multiple instances to coexist without message conflicts. Maximum length is 20 characters.
    /// Default is "v1".
    /// </summary>
    public string Version { get; set; } = "v1";

    /// <summary>
    /// Gets or sets the time interval (in seconds) after which successfully processed messages are automatically deleted.
    /// This helps manage storage by removing old successfully delivered messages.
    /// Default is 86,400 seconds (24 hours).
    /// </summary>
    public int SucceedMessageExpiredAfter { get; set; } = 24 * 3600;

    /// <summary>
    /// Gets or sets the time interval (in seconds) after which failed messages are automatically deleted.
    /// This allows cleanup of old failed messages that exceed the retry threshold.
    /// Default is 1,296,000 seconds (15 days).
    /// </summary>
    public int FailedMessageExpiredAfter { get; set; } = 15 * 24 * 3600;

    /// <summary>
    /// Gets or sets the number of concurrent consumer threads for message consumption from the transport.
    /// Higher values increase parallelism but consume more resources; lower values reduce resource usage but may lower throughput.
    /// Default is 1.
    /// </summary>
    public int ConsumerThreadCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether to enable parallel execution of subscriber methods using an in-memory queue.
    /// When enabled, received messages are buffered in memory and processed concurrently by multiple worker threads.
    /// Use <see cref="SubscriberParallelExecuteThreadCount"/> to configure the number of parallel threads.
    /// Default is false.
    /// </summary>
    public bool EnableSubscriberParallelExecute { get; set; }

    /// <summary>
    /// Gets or sets the number of parallel worker threads for subscriber message execution when <see cref="EnableSubscriberParallelExecute"/> is enabled.
    /// This controls the degree of parallelism when processing subscriber handlers.
    /// Default is the number of logical processors (<see cref="Environment.ProcessorCount"/>).
    /// </summary>
    public int SubscriberParallelExecuteThreadCount { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Gets or sets a multiplier factor for determining the in-memory buffer capacity when <see cref="EnableSubscriberParallelExecute"/> is enabled.
    /// The actual buffer capacity is calculated as: <c>SubscriberParallelExecuteThreadCount × SubscriberParallelExecuteBufferFactor</c>.
    /// This controls how many messages can be queued before blocking new incoming messages.
    /// Default is 1.
    /// </summary>
    public int SubscriberParallelExecuteBufferFactor { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether to enable parallel execution of publish operations using the .NET thread pool.
    /// When enabled, message publishing tasks are dispatched to the thread pool for concurrent execution, improving throughput for high-volume publishing scenarios.
    /// Default is false.
    /// </summary>
    public bool EnablePublishParallelSend { get; set; }

    /// <summary>
    /// Gets or sets the batch size for parallel message sending when <see cref="EnablePublishParallelSend"/> is enabled.
    /// When null, the batch size is automatically calculated using a logarithmic formula based on channel capacity.
    /// The automatic calculation uses: Math.Min(500, Math.Max(10, (int)Math.Log2(channelSize) * 10)).
    /// Use this property to override the automatic calculation for custom tuning.
    /// Valid range: 1-500. Values outside this range will be clamped.
    /// Default is null (auto-calculate).
    /// </summary>
    public int? PublishBatchSize { get; set; }

    /// <summary>
    /// Gets or sets the interval (in seconds) at which the cleanup processor removes expired messages from the message storage.
    /// The processor runs periodically to clean up messages that have exceeded their expiration times.
    /// Default is 300 seconds (5 minutes).
    /// </summary>
    public int CollectorCleaningInterval { get; set; } = 300;

    /// <summary>
    /// Gets or sets the maximum number of delayed or failed messages to fetch in a single scheduler cycle.
    /// Larger batches improve throughput but consume more memory; smaller batches reduce memory usage but may lower throughput.
    /// Default is 1,000.
    /// </summary>
    public int SchedulerBatchSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the JSON serialization options used for message content serialization and deserialization.
    /// Customize this to control JSON formatting, naming policies, converters, and other serialization behavior.
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions { get; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether to use distributed storage locking when retrying failed messages.
    /// When enabled, only one instance in a distributed system will perform message retries, preventing duplicate processing.
    /// This is essential for clustered deployments to ensure exactly-once retry semantics.
    /// Default is false.
    /// </summary>
    public bool UseStorageLock { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether publish calls require a resolved tenant identifier.
    /// When <see langword="true"/>, the publish wrapper rejects calls where neither
    /// <see cref="PublishOptions.TenantId"/> nor the ambient <c>ICurrentTenant.Id</c> resolves a
    /// tenant, throwing <see cref="MissingTenantContextException"/>. Sibling of the EF write guard
    /// (#234) and the Mediator behavior (#236) for cross-layer tenant safety.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="false"/> to preserve today's behavior. Background workers and
    /// <c>IHostedService</c> callers without an ambient request scope must wrap publishes in
    /// <c>using (currentTenant.Change(tenantId))</c> or set <see cref="PublishOptions.TenantId"/>
    /// explicitly when this flag is enabled.
    /// </remarks>
    public bool TenantContextRequired { get; set; }

    /// <summary>
    /// Gets the global circuit breaker configuration that applies to all consumer groups.
    /// Individual consumers may override specific properties via
    /// <see cref="IConsumerBuilder{TConsumer}.WithCircuitBreaker"/>.
    /// </summary>
    public CircuitBreakerOptions CircuitBreaker { get; } = new();

    /// <summary>
    /// Gets or sets retry policy configuration for inline and persisted retries.
    /// </summary>
    public RetryPolicyOptions RetryPolicy { get; set; } = new();

    /// <summary>
    /// Gets the retry processor configuration that controls adaptive polling and backpressure behavior
    /// when the circuit breaker is engaged.
    /// </summary>
    public RetryProcessorOptions RetryProcessor { get; } = new();

    /// <summary>
    /// Registers a messaging options extension that will be executed when configuring messaging services.
    /// Extensions allow third-party libraries to customize messaging behavior without modifying core configuration.
    /// </summary>
    /// <param name="extension">The extension instance to register.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="extension"/> is null.</exception>
    public void RegisterExtension(IMessagesOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }

    /// <inheritdoc />
    public IMessagingBuilder SubscribeFromAssembly(Assembly assembly)
    {
        Argument.IsNotNull(assembly);
        Argument.IsNotNull(Services, "Services must be initialized before calling SubscribeFromAssembly");
        Argument.IsNotNull(Registry, "Registry must be initialized before calling SubscribeFromAssembly");

        // Single pass: filter types and cache their IConsume<T> interfaces
        var consumerTypesWithInterfaces = assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .Select(t =>
            {
                var interfaces = t.GetInterfaces();
                var consumeInterfaces = interfaces
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsume<>))
                    .ToList();

                return new { Type = t, ConsumeInterfaces = consumeInterfaces };
            })
            .Where(x => x.ConsumeInterfaces.Count > 0)
            .ToList();

        foreach (var consumer in consumerTypesWithInterfaces)
        {
            foreach (var consumeInterface in consumer.ConsumeInterfaces)
            {
                var messageType = consumeInterface.GetGenericArguments()[0];

                // Register consumer with default configuration
                RegisterConsumer(consumer.Type, messageType, topic: null, group: null, concurrency: 1);
            }
        }

        return this;
    }

    /// <inheritdoc />
    public IMessagingBuilder SubscribeFromAssemblyContaining<TMarker>()
    {
        return SubscribeFromAssembly(typeof(TMarker).Assembly);
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> Subscribe<TConsumer>()
        where TConsumer : class
    {
        Argument.IsNotNull(Registry, "Registry must be initialized before calling Subscribe");

        var messageType = _ResolveExplicitMessageType(typeof(TConsumer));

        var metadata = RegisterConsumer(typeof(TConsumer), messageType, topic: null, group: null, concurrency: 1);

        return new ConsumerBuilder<TConsumer>(this, Registry, CircuitBreakerRegistry, metadata, autoRegistered: true);
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> Subscribe<TConsumer>(string topic)
        where TConsumer : class
    {
        Argument.IsNotNullOrWhiteSpace(topic);
        Argument.IsNotNull(Registry, "Registry must be initialized before calling Subscribe");

        var messageType = _ResolveExplicitMessageType(typeof(TConsumer));

        // Automatically create topic mapping
        _WithTopicMapping(messageType, topic);

        // Immediately register with default settings (concurrency=1, group=null)
        var metadata = RegisterConsumer(typeof(TConsumer), messageType, topic, group: null, concurrency: 1);

        // Return builder that can update the registration if further configuration is needed
        return new ConsumerBuilder<TConsumer>(
            this,
            Registry,
            CircuitBreakerRegistry,
            metadata,
            topic,
            autoRegistered: true
        );
    }

    /// <inheritdoc />
    public IMessagingBuilder Subscribe<TConsumer>(Action<IConsumerBuilder<TConsumer>> configure)
        where TConsumer : class
    {
        Argument.IsNotNull(configure);

        configure(Subscribe<TConsumer>());
        return this;
    }

    /// <inheritdoc />
    public IMessagingBuilder WithTopicMapping<TMessage>(string topic)
        where TMessage : class
    {
        Argument.IsNotNullOrWhiteSpace(topic);

        _WithTopicMapping(typeof(TMessage), topic);
        return this;
    }

    /// <inheritdoc />
    public IMessagingBuilder UseConventions(Action<MessagingConventions> configure)
    {
        Argument.IsNotNull(configure);

        configure(Conventions);
        Version = Conventions.Version;
        return this;
    }

    /// <summary>
    /// Registers a topic mapping for a message type (non-generic version for internal use).
    /// </summary>
    internal void _WithTopicMapping(Type messageType, string topic)
    {
        Argument.IsNotNullOrWhiteSpace(topic);
        _ValidateTopicName(topic);

        if (TopicMappings.TryGetValue(messageType, out var existingTopic) && existingTopic != topic)
        {
            throw new InvalidOperationException(
                $"Message type {messageType.Name} is already mapped to topic '{existingTopic}'. Cannot map to '{topic}'."
            );
        }

        TopicMappings[messageType] = topic;
    }

    internal string ApplyTopicNamePrefix(string topic)
    {
        Argument.IsNotNullOrWhiteSpace(topic);

        return string.IsNullOrWhiteSpace(TopicNamePrefix) ? topic : string.Concat(TopicNamePrefix, ".", topic);
    }

    internal string ApplyGroupNamePrefix(string group)
    {
        Argument.IsNotNullOrWhiteSpace(group);

        return string.IsNullOrWhiteSpace(GroupNamePrefix) ? group : string.Concat(GroupNamePrefix, ".", group);
    }

    private static Type _ResolveExplicitMessageType(Type consumerType)
    {
        var consumeInterfaces = consumerType
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsume<>))
            .ToList();

        return consumeInterfaces.Count switch
        {
            0 => throw new InvalidOperationException($"{consumerType.Name} does not implement IConsume<T>"),
            > 1 => throw new InvalidOperationException(
                $"{consumerType.Name} implements multiple IConsume<T> interfaces. "
                    + "Use SubscribeFromAssembly(...) for multi-message consumers."
            ),
            _ => consumeInterfaces[0].GetGenericArguments()[0],
        };
    }

    /// <summary>
    /// Validates topic name format and constraints.
    /// </summary>
    private static void _ValidateTopicName(string topic)
    {
        const int maxTopicLength = 255;

        if (topic.Length > maxTopicLength)
        {
            throw new ArgumentException(
                $"Topic name '{topic}' exceeds maximum length of {maxTopicLength} characters.",
                nameof(topic)
            );
        }

        if (topic.StartsWith('.') || topic.EndsWith('.'))
        {
            throw new ArgumentException($"Topic name '{topic}' cannot start or end with a dot.", nameof(topic));
        }

        if (topic.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Topic name '{topic}' cannot contain consecutive dots.", nameof(topic));
        }

        foreach (var c in topic)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_')
            {
                throw new ArgumentException(
                    $"Topic name '{topic}' contains invalid character '{c}'. Only alphanumeric characters, dots, hyphens, and underscores are allowed.",
                    nameof(topic)
                );
            }
        }
    }

    internal ConsumerMetadata CreateConsumerMetadata(
        Type consumerType,
        Type messageType,
        string? topic,
        string? group,
        byte concurrency,
        string? handlerId = null
    )
    {
        var conventions = Conventions;
        conventions.Version = Version;

        var finalHandlerId = handlerId ?? MessagingConventions.GetDefaultHandlerId(consumerType, messageType);
        var resolvedTopic =
            topic
            ?? (TopicMappings.TryGetValue(messageType, out var mappedTopic) ? mappedTopic : null)
            ?? conventions.GetTopicName(messageType)
            ?? messageType.Name;
        var finalTopic = ApplyTopicNamePrefix(resolvedTopic);
        var finalGroup = ResolveGroupName(finalHandlerId, group);

        return new ConsumerMetadata(messageType, consumerType, finalTopic, finalGroup, concurrency, finalHandlerId);
    }

    internal string ResolveGroupName(string handlerId, string? explicitGroup = null)
    {
        Argument.IsNotNullOrWhiteSpace(handlerId);

        Conventions.Version = Version;

        var resolvedGroup =
            !string.IsNullOrWhiteSpace(explicitGroup) ? explicitGroup!
            : !string.IsNullOrWhiteSpace(Conventions.DefaultGroup) ? Conventions.DefaultGroup!
            : IsDefaultGroupNameConfigured ? DefaultGroupName
            : Conventions.GetGroupName(handlerId);

        return ApplyGroupNamePrefix(resolvedGroup);
    }

    /// <summary>
    /// Registers a consumer with the specified metadata.
    /// </summary>
    internal ConsumerMetadata RegisterConsumer(
        Type consumerType,
        Type messageType,
        string? topic,
        string? group,
        byte concurrency
    )
    {
        Argument.IsNotNull(Services, "Services must be initialized before calling _RegisterConsumer");
        Argument.IsNotNull(Registry, "Registry must be initialized before calling _RegisterConsumer");

        var metadata = CreateConsumerMetadata(consumerType, messageType, topic, group, concurrency);

        Registry.Register(metadata);

        Services.TryAdd(new ServiceDescriptor(consumerType, consumerType, ServiceLifetime.Scoped));

        var serviceType = typeof(IConsume<>).MakeGenericType(messageType);
        Services.TryAdd(
            new ServiceDescriptor(serviceType, sp => sp.GetRequiredService(consumerType), ServiceLifetime.Scoped)
        );

        return metadata;
    }
}

internal sealed class MessagingOptionsValidator : AbstractValidator<MessagingOptions>
{
    public MessagingOptionsValidator()
    {
        RuleFor(x => x.RetryPolicy).NotNull().SetValidator(new RetryPolicyOptionsValidator());
    }
}
