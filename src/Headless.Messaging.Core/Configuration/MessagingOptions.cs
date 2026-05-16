// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using FluentValidation;
using Headless.Checks;
using Headless.Messaging.CircuitBreaker;

namespace Headless.Messaging.Configuration;

/// <summary>
/// Provides options to customize various aspects of the message processing pipeline. This includes settings for message expiration,
/// retry mechanisms, concurrency management, and serialization, among others. This class allows fine-tuning
/// messaging behavior to better align with specific application requirements, such as adjusting threading models for
/// subscriber message processing, setting message expiry times, and customizing serialization settings.
/// </summary>
/// <remarks>
/// <see cref="MessagingOptions"/> is a pure runtime configuration bag — it is registered through the
/// standard <c>IOptions&lt;T&gt;</c> pipeline. Setup-time state (the service collection, consumer registry,
/// circuit-breaker registry, options-extension list) lives on <see cref="MessagingSetupBuilder"/> so it
/// cannot leak into the runtime instance. The <c>CopyTo</c> below must propagate every public mutable
/// property; a reflection-based test guards against drift.
/// </remarks>
[PublicAPI]
public sealed class MessagingOptions
{
#pragma warning disable IDE0032
    private string _defaultGroupName =
        "headless.queue." + Assembly.GetEntryAssembly()?.GetName().Name!.ToLower(CultureInfo.InvariantCulture);
#pragma warning restore IDE0032

    internal Dictionary<Type, string> TopicMappings { get; } = new();
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

    private RetryPolicyOptions _retryPolicy = new();

    /// <summary>
    /// Gets retry policy configuration for inline and persisted retries.
    /// </summary>
    public RetryPolicyOptions RetryPolicy => _retryPolicy;

    /// <summary>
    /// Gets the retry processor configuration that controls adaptive polling and backpressure behavior
    /// when the circuit breaker is engaged.
    /// </summary>
    public RetryProcessorOptions RetryProcessor { get; } = new();

    /// <summary>
    /// Copies all public and internal-settable runtime properties of this instance to <paramref name="target"/>.
    /// Also copies nested options via their own <c>CopyTo</c> methods and replicates collection state.
    /// </summary>
    /// <remarks>
    /// MAINTENANCE NOTE: any new public mutable property added to <see cref="MessagingOptions"/> must be
    /// added here. The reflection-based drift test in <c>MessagingOptionsCopyToTests</c> will fail otherwise.
    /// </remarks>
    internal void CopyTo(MessagingOptions target)
    {
        target.DefaultGroupName = DefaultGroupName;
        target.IsDefaultGroupNameConfigured = IsDefaultGroupNameConfigured;
        target.GroupNamePrefix = GroupNamePrefix;
        target.TopicNamePrefix = TopicNamePrefix;
        target.Version = Version;
        target.Conventions = Conventions;
        target.SucceedMessageExpiredAfter = SucceedMessageExpiredAfter;
        target.FailedMessageExpiredAfter = FailedMessageExpiredAfter;
        target.ConsumerThreadCount = ConsumerThreadCount;
        target.EnableSubscriberParallelExecute = EnableSubscriberParallelExecute;
        target.SubscriberParallelExecuteThreadCount = SubscriberParallelExecuteThreadCount;
        target.SubscriberParallelExecuteBufferFactor = SubscriberParallelExecuteBufferFactor;
        target.EnablePublishParallelSend = EnablePublishParallelSend;
        target.PublishBatchSize = PublishBatchSize;
        target.CollectorCleaningInterval = CollectorCleaningInterval;
        target.SchedulerBatchSize = SchedulerBatchSize;
        target.UseStorageLock = UseStorageLock;
        target.TenantContextRequired = TenantContextRequired;
        _CopyJsonSerializerOptions(JsonSerializerOptions, target.JsonSerializerOptions);
        RetryPolicy.CopyTo(target.RetryPolicy);
        CircuitBreaker.CopyTo(target.CircuitBreaker);
        RetryProcessor.CopyTo(target.RetryProcessor);

        foreach (var mapping in TopicMappings)
        {
            target.TopicMappings[mapping.Key] = mapping.Value;
        }
    }

    /// <summary>
    /// Copies the mutable fields of <paramref name="source"/> onto <paramref name="target"/>.
    /// Required because <see cref="JsonSerializerOptions"/> is a get-only property — we can't
    /// swap the reference, so we copy the user-configured fields onto the DI-resolved instance.
    /// </summary>
    private static void _CopyJsonSerializerOptions(JsonSerializerOptions source, JsonSerializerOptions target)
    {
        target.AllowOutOfOrderMetadataProperties = source.AllowOutOfOrderMetadataProperties;
        target.AllowTrailingCommas = source.AllowTrailingCommas;
        target.DefaultBufferSize = source.DefaultBufferSize;
        target.DefaultIgnoreCondition = source.DefaultIgnoreCondition;
        target.DictionaryKeyPolicy = source.DictionaryKeyPolicy;
        target.Encoder = source.Encoder;
        target.IgnoreReadOnlyFields = source.IgnoreReadOnlyFields;
        target.IgnoreReadOnlyProperties = source.IgnoreReadOnlyProperties;
        target.IncludeFields = source.IncludeFields;
        target.MaxDepth = source.MaxDepth;
        target.NumberHandling = source.NumberHandling;
        target.PreferredObjectCreationHandling = source.PreferredObjectCreationHandling;
        target.PropertyNameCaseInsensitive = source.PropertyNameCaseInsensitive;
        target.PropertyNamingPolicy = source.PropertyNamingPolicy;
        target.ReadCommentHandling = source.ReadCommentHandling;
        target.ReferenceHandler = source.ReferenceHandler;
        target.RespectNullableAnnotations = source.RespectNullableAnnotations;
        target.RespectRequiredConstructorParameters = source.RespectRequiredConstructorParameters;
        target.TypeInfoResolver = source.TypeInfoResolver;
        target.UnknownTypeHandling = source.UnknownTypeHandling;
        target.UnmappedMemberHandling = source.UnmappedMemberHandling;
        target.WriteIndented = source.WriteIndented;

        foreach (var converter in source.Converters)
        {
            target.Converters.Add(converter);
        }

        foreach (var modifier in source.TypeInfoResolverChain)
        {
            if (!ReferenceEquals(modifier, source.TypeInfoResolver))
            {
                target.TypeInfoResolverChain.Add(modifier);
            }
        }
    }

    /// <summary>
    /// Registers a topic mapping for a message type. Used by <see cref="MessagingSetupBuilder"/>.
    /// </summary>
    internal void WithTopicMapping(Type messageType, string topic)
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
}

internal sealed class MessagingOptionsValidator : AbstractValidator<MessagingOptions>
{
    public MessagingOptionsValidator()
    {
        RuleFor(x => x.RetryPolicy).NotNull().SetValidator(new RetryPolicyOptionsValidator());
    }
}
