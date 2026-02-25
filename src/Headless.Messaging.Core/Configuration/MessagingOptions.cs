// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Cronos;
using Headless.Checks;
using Headless.Messaging.Messages;
using Headless.Messaging.Retry;
using Headless.Messaging.Scheduling;
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
    internal IServiceCollection? Services { get; set; }
    internal ConsumerRegistry? Registry { get; set; }
    internal Dictionary<Type, string> TopicMappings { get; } = new();
    internal IList<IMessagesOptionsExtension> Extensions { get; } = new List<IMessagesOptionsExtension>();
    internal Headless.Messaging.MessagingConventions? Conventions { get; set; }
    internal List<ScheduledJobDefinition> ScheduledJobDefinitions { get; } = [];

    /// <summary>
    /// Gets or sets the default consumer group name for subscribers.
    /// In Kafka, this corresponds to the consumer group name; in RabbitMQ, it corresponds to the queue name.
    /// Default value is "headless.queue." followed by the entry assembly name in lowercase.
    /// </summary>
    public string DefaultGroupName { get; set; } =
        "headless.queue." + Assembly.GetEntryAssembly()?.GetName().Name!.ToLower(CultureInfo.InvariantCulture);

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
    /// Gets or sets the polling interval (in seconds) for the retry processor to check and retry failed messages.
    /// Default is 60 seconds.
    /// </summary>
    public int FailedRetryInterval { get; set; } = 60;

    /// <summary>
    /// Gets or sets an optional callback function invoked when a message has been retried the maximum number of times
    /// specified by <see cref="FailedRetryCount"/> without success. This callback receives detailed information about the failed message.
    /// </summary>
    public Action<FailedInfo>? FailedThresholdCallback { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for failed messages (both published and subscribed).
    /// Once this threshold is reached, the message is marked as permanently failed and no longer retried.
    /// Default is 50 times.
    /// </summary>
    public int FailedRetryCount { get; set; } = 50;

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
    /// Gets or sets the lookback time window (in seconds) for the retry processor to pick up scheduled or failed status messages.
    /// This ensures that messages with clocks slightly out of sync are still processed correctly.
    /// Default is 240 seconds (4 minutes).
    /// </summary>
    public int FallbackWindowLookbackSeconds { get; set; } = 240;

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
    /// Gets or sets the retry backoff strategy used to calculate retry delays and determine retry eligibility.
    /// When null, defaults to <see cref="ExponentialBackoffStrategy"/> with 1s initial delay, 5min max delay, and 2x multiplier.
    /// For fixed interval retries, use <see cref="FixedIntervalBackoffStrategy"/>.
    /// </summary>
    public IRetryBackoffStrategy RetryBackoffStrategy { get; set; } = new ExponentialBackoffStrategy();

    /// <summary>
    /// Gets or sets a value indicating whether strict startup/runtime guardrails are enforced.
    /// When enabled (default), invalid topic/group names and invalid scheduling metadata fail fast.
    /// Set to <c>false</c> only for controlled migrations.
    /// </summary>
    public bool StrictValidation { get; set; } = true;

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
    public IMessagingBuilder ScanConsumers(Assembly assembly)
    {
        Argument.IsNotNull(assembly);
        Argument.IsNotNull(Services, "Services must be initialized before calling ScanConsumers");
        Argument.IsNotNull(Registry, "Registry must be initialized before calling ScanConsumers");

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

                // Check for [Recurring] attribute on IConsume<ScheduledTrigger> handlers
                if (messageType == typeof(ScheduledTrigger))
                {
                    var recurring = consumer.Type.GetCustomAttribute<RecurringAttribute>();
                    if (recurring is not null)
                    {
                        _RegisterRecurringConsumer(consumer.Type, recurring);
                        continue;
                    }
                }

                // Register consumer with default configuration
                RegisterConsumer(consumer.Type, messageType, topic: null, group: null, concurrency: 1);
            }
        }

        return this;
    }

    /// <summary>
    /// Registers a recurring scheduled consumer as a keyed DI service and collects its job definition.
    /// </summary>
    internal void _RegisterRecurringConsumer(Type consumerType, RecurringAttribute recurring)
    {
        Argument.IsNotNull(Services, "Services must be initialized before calling _RegisterRecurringConsumer");

        if (StrictValidation)
        {
            _ValidateRecurringDefinition(recurring);
        }

        var jobName = recurring.Name ?? _DeriveJobName(consumerType);

        // Register as keyed service for dispatch by job name
        Services.TryAddKeyedScoped(typeof(IConsume<ScheduledTrigger>), jobName, consumerType);

        // Collect job definition for startup reconciliation
        ScheduledJobDefinitions.Add(
            new ScheduledJobDefinition
            {
                Name = jobName,
                ConsumerType = consumerType,
                CronExpression = recurring.CronExpression,
                TimeZone = recurring.TimeZone,
                RetryIntervals = recurring.RetryIntervals,
                SkipIfRunning = recurring.SkipIfRunning,
                Timeout = recurring.TimeoutSeconds > 0 ? TimeSpan.FromSeconds(recurring.TimeoutSeconds) : null,
                MisfireStrategy = recurring.MisfireStrategy,
            }
        );
    }

    /// <summary>
    /// Derives a job name from a consumer type by stripping common suffixes.
    /// </summary>
    internal static string _DeriveJobName(Type consumerType)
    {
        var name = consumerType.Name;

        if (name.EndsWith("Consumer", StringComparison.Ordinal))
        {
            return name[..^"Consumer".Length];
        }

        if (name.EndsWith("Job", StringComparison.Ordinal))
        {
            return name[..^"Job".Length];
        }

        return name;
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> Consumer<TConsumer>()
        where TConsumer : class
    {
        Argument.IsNotNull(Registry, "Registry must be initialized before calling Consumer");

        // Find IConsume<T> interface
        var consumeInterface = typeof(TConsumer)
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsume<>));

        if (consumeInterface == null)
        {
            throw new InvalidOperationException($"{typeof(TConsumer).Name} does not implement IConsume<T>");
        }

        var messageType = consumeInterface.GetGenericArguments()[0];

        return new ConsumerBuilder<TConsumer>(this, Registry, messageType);
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> Consumer<TConsumer>(string topic)
        where TConsumer : class
    {
        Argument.IsNotNullOrWhiteSpace(topic);
        Argument.IsNotNull(Registry, "Registry must be initialized before calling Consumer");

        // Find IConsume<T> interface
        var consumeInterface = typeof(TConsumer)
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsume<>));

        if (consumeInterface == null)
        {
            throw new InvalidOperationException($"{typeof(TConsumer).Name} does not implement IConsume<T>");
        }

        var messageType = consumeInterface.GetGenericArguments()[0];

        // Automatically create topic mapping
        _WithTopicMapping(messageType, topic);

        // Immediately register with default settings (concurrency=1, group=null)
        RegisterConsumer(typeof(TConsumer), messageType, topic, group: null, concurrency: 1);

        // Return builder that can update the registration if further configuration is needed
        return new ConsumerBuilder<TConsumer>(this, Registry, messageType, topic, autoRegistered: true);
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
    public IMessagingBuilder ConfigureConventions(Action<Headless.Messaging.MessagingConventions> configure)
    {
        Argument.IsNotNull(configure);

        Conventions ??= new Headless.Messaging.MessagingConventions();
        configure(Conventions);
        return this;
    }

    /// <summary>
    /// Registers a topic mapping for a message type (non-generic version for internal use).
    /// </summary>
    internal void _WithTopicMapping(Type messageType, string topic)
    {
        Argument.IsNotNullOrWhiteSpace(topic);
        if (StrictValidation)
        {
            ValidateTopicName(topic);
        }

        if (TopicMappings.TryGetValue(messageType, out var existingTopic) && existingTopic != topic)
        {
            throw new InvalidOperationException(
                $"Message type {messageType.Name} is already mapped to topic '{existingTopic}'. Cannot map to '{topic}'."
            );
        }

        TopicMappings[messageType] = topic;
    }

    /// <summary>
    /// Validates topic name format and constraints.
    /// </summary>
    internal static void ValidateTopicName(string topic)
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

        if (topic.Contains(".."))
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

    internal static void ValidateSubscriptionTopicName(string topic)
    {
        Argument.IsNotNullOrWhiteSpace(topic);

        if (topic.IndexOf('*') < 0 && topic.IndexOf('#') < 0)
        {
            ValidateTopicName(topic);
            return;
        }

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

        if (topic.Contains(".."))
        {
            throw new ArgumentException($"Topic name '{topic}' cannot contain consecutive dots.", nameof(topic));
        }

        foreach (var c in topic)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_' && c != '*' && c != '#')
            {
                throw new ArgumentException(
                    $"Topic name '{topic}' contains invalid character '{c}'. Only alphanumeric characters, dots, hyphens, underscores, and wildcards (*,#) are allowed.",
                    nameof(topic)
                );
            }
        }
    }

    internal static void ValidateGroupName(string group)
    {
        Argument.IsNotNullOrWhiteSpace(group);

        const int maxGroupLength = 255;
        if (group.Length > maxGroupLength)
        {
            throw new ArgumentException(
                $"Group name '{group}' exceeds maximum length of {maxGroupLength} characters.",
                nameof(group)
            );
        }

        if (group.StartsWith('.') || group.EndsWith('.'))
        {
            throw new ArgumentException($"Group name '{group}' cannot start or end with a dot.", nameof(group));
        }

        if (group.Contains(".."))
        {
            throw new ArgumentException($"Group name '{group}' cannot contain consecutive dots.", nameof(group));
        }

        foreach (var c in group)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_')
            {
                throw new ArgumentException(
                    $"Group name '{group}' contains invalid character '{c}'. Only alphanumeric characters, dots, hyphens, and underscores are allowed.",
                    nameof(group)
                );
            }
        }
    }

    /// <summary>
    /// Registers a consumer with the specified metadata.
    /// </summary>
    internal void RegisterConsumer(Type consumerType, Type messageType, string? topic, string? group, byte concurrency)
    {
        Argument.IsNotNull(Services, "Services must be initialized before calling _RegisterConsumer");
        Argument.IsNotNull(Registry, "Registry must be initialized before calling _RegisterConsumer");

        // Determine the topic name
        var finalTopic =
            topic
            ?? (TopicMappings.TryGetValue(messageType, out var mappedTopic) ? mappedTopic : null)
            ?? Conventions?.GetTopicName(messageType)
            ?? messageType.Name; // Fallback to message type name

        // Determine the group name
        var finalGroup = group ?? Conventions?.DefaultGroup;

        if (StrictValidation)
        {
            ValidateSubscriptionTopicName(finalTopic);
            if (!string.IsNullOrWhiteSpace(finalGroup))
            {
                ValidateGroupName(finalGroup);
            }
        }

        // Create metadata
        var metadata = new ConsumerMetadata(messageType, consumerType, finalTopic, finalGroup, concurrency);

        // Register in registry
        Registry.Register(metadata);

        // Register consumer in DI as scoped service
        var serviceType = typeof(IConsume<>).MakeGenericType(messageType);
        Services.TryAddScoped(serviceType, consumerType);
    }

    private static void _ValidateRecurringDefinition(RecurringAttribute recurring)
    {
        Argument.IsNotNull(recurring);
        Argument.IsNotNullOrWhiteSpace(recurring.CronExpression);

        _ = CronExpression.Parse(recurring.CronExpression, CronFormat.IncludeSeconds);

        if (!string.IsNullOrWhiteSpace(recurring.TimeZone))
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(recurring.TimeZone);
        }
    }
}
