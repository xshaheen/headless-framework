// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using FluentValidation;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Registration;

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
    /// Gets or sets an optional prefix to be prepended to all message names.
    /// </summary>
    public string? MessageNamePrefix { get; set; }

    /// <summary>
    /// Gets or sets the version identifier for messages, used to isolate data between different instances or deployments.
    /// This allows multiple instances to coexist without message conflicts. Maximum length is 20 characters.
    /// Default is "v1".
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Version"/> also acts as the cross-process isolation key for the messaging
    /// distributed-lock resources. The two retry-pickup loops acquire locks named
    /// <c>messaging.publish-retry-{Version}</c> and <c>messaging.receive-retry-{Version}</c>
    /// (see <see cref="Headless.Messaging.Internal.MessagingKeys.PublishRetryResource"/> and
    /// <see cref="Headless.Messaging.Internal.MessagingKeys.ReceiveRetryResource"/>).
    /// </para>
    /// <para>
    /// If two distinct messaging services share a single lock store (for example, two apps pointed
    /// at the same Redis), they MUST set distinct <see cref="Version"/> values — otherwise their
    /// retry processors will fight over the same lock resource and starve each other. The default
    /// <c>"v1"</c> is only safe for a single-service deployment.
    /// </para>
    /// <para>
    /// See also <c>docs/llms/messaging.md</c> for the deployment guidance.
    /// </para>
    /// </remarks>
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
    /// Gets or sets the maximum number of messages leased in a single retry-pickup batch.
    /// Larger batches process more per cycle but increase memory and lock contention; smaller batches
    /// reduce contention but may lower retry throughput. Default is 200.
    /// </summary>
    public int RetryBatchSize { get; set; } = 200;

    /// <summary>
    /// Gets or sets the JSON serialization options used for message content serialization and deserialization.
    /// Customize this to control JSON formatting, naming policies, converters, and other serialization behavior.
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions { get; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether to use distributed storage locking when retrying failed messages.
    /// When enabled, retry processors coordinate pickup through a messaging-keyed distributed lock,
    /// reducing duplicate retry-pickup work across replicas. Message delivery remains at-least-once and
    /// consumers must stay idempotent.
    /// Default is false.
    /// </summary>
    public bool UseStorageLock { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether publish calls require a resolved tenant identifier.
    /// When <see langword="true"/>, the publish wrapper rejects calls where neither
    /// <see cref="MessageOptions.TenantId"/> nor the ambient <c>ICurrentTenant.Id</c> resolves a
    /// tenant, throwing <see cref="MissingTenantContextException"/>. Sibling of the EF write guard
    /// (#234) and the HTTP authorization requirement for cross-layer tenant safety.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="false"/> to preserve today's behavior. Background workers and
    /// <c>IHostedService</c> callers without an ambient request scope must wrap publishes in
    /// <c>using (currentTenant.Change(tenantId))</c> or set <see cref="MessageOptions.TenantId"/>
    /// explicitly when this flag is enabled.
    /// </remarks>
    public bool TenantContextRequired { get; set; }

    /// <summary>
    /// Gets or sets the maximum time the framework waits for a transport publish before treating it as a failed attempt.
    /// Default is 10 seconds.
    /// </summary>
    /// <remarks>
    /// The timeout is linked with host shutdown. Some broker clients do not fully honor cancellation
    /// while publishing; this timeout is the framework-level bound for cooperative transports.
    /// </remarks>
    public TimeSpan TransportPublishTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the ADO.NET command timeout applied by SQL-backed messaging storage providers.
    /// Default is 30 seconds.
    /// </summary>
    /// <remarks>
    /// Terminal state writes intentionally use <see cref="CancellationToken.None"/> after cancellation
    /// classification so shutdown cannot orphan a final state transition. This timeout is the wall-clock
    /// safety net for those commands.
    /// </remarks>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the maximum time the commit-coordination drain spends flushing buffered outbox messages to
    /// the transport after a transaction commits. Default is 30 seconds.
    /// </summary>
    /// <remarks>
    /// The post-commit drain runs with <see cref="CancellationToken.None"/> (a committed dispatch must not be
    /// abandoned because the request was cancelled), so an unresponsive broker would otherwise hold the drain —
    /// and the request thread, DI scope, and DB connection — indefinitely. This timeout bounds that wait;
    /// messages are already durably stored in-transaction, so any not dispatched before the deadline are
    /// recovered by the relay sweep (dispatch is acceleration, not correctness).
    /// </remarks>
    public TimeSpan OutboxFlushTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the maximum time shutdown waits for messaging background loops and in-flight
    /// handlers to observe cancellation. Default is 30 seconds.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the cadence of the dead-owner recovery reconcile backstop. Default is 1 minute.
    /// </summary>
    /// <remarks>
    /// The dead-owner recovery bridge reclaims orphaned outbox/inbox rows on two triggers: a low-latency
    /// <c>NodeLeft</c> membership-watch path and this periodic liveness-snapshot reconcile. The reconcile is
    /// the authoritative backstop that catches any death missed while the watch loop was not subscribed; the
    /// watch path is best-effort acceleration. This cadence does not bound correctness — the per-row
    /// <c>LockedUntil</c> lease floor recovers any row independently — so it can safely run longer than the
    /// retry-poll interval. Mirrors <c>SchedulerOptionsBuilder.DeadNodeReconcileInterval</c> on the Jobs side.
    /// </remarks>
    public TimeSpan DeadNodeReconcileInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets the global circuit breaker configuration that applies to all consumer groups.
    /// Individual consumers may override specific properties via
    /// <see cref="IConsumerBuilderBase{TConsumer,TBuilder}.WithCircuitBreaker"/>.
    /// </summary>
    public CircuitBreakerOptions CircuitBreaker { get; } = new();

    /// <summary>
    /// Gets retry policy configuration for inline and persisted retries. Mutate the returned
    /// instance's properties; the property itself is get-only and cannot be replaced.
    /// </summary>
    /// <remarks>
    /// The non-null guarantee is enforced by <c>MessagingOptionsValidator</c> via
    /// <c>ValidateOnStart()</c>; the property never observes a null value at runtime for callers
    /// going through the standard <c>IOptions&lt;T&gt;</c> pipeline.
    /// </remarks>
    public RetryPolicyOptions RetryPolicy { get; } = new();

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
        target.MessageNamePrefix = MessageNamePrefix;
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
        target.TransportPublishTimeout = TransportPublishTimeout;
        target.CommandTimeout = CommandTimeout;
        target.OutboxFlushTimeout = OutboxFlushTimeout;
        target.ShutdownTimeout = ShutdownTimeout;
        target.DeadNodeReconcileInterval = DeadNodeReconcileInterval;
        _CopyJsonSerializerOptions(JsonSerializerOptions, target.JsonSerializerOptions);
        RetryPolicy.CopyTo(target.RetryPolicy);
        CircuitBreaker.CopyTo(target.CircuitBreaker);
        RetryProcessor.CopyTo(target.RetryProcessor);
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

    internal string ApplyMessageNamePrefix(string messageName)
    {
        Argument.IsNotNullOrWhiteSpace(messageName);

        return string.IsNullOrWhiteSpace(MessageNamePrefix) ? messageName : $"{MessageNamePrefix}.{messageName}";
    }

    internal string ApplyGroupNamePrefix(string group)
    {
        Argument.IsNotNullOrWhiteSpace(group);

        return string.IsNullOrWhiteSpace(GroupNamePrefix) ? group : $"{GroupNamePrefix}.{group}";
    }

    internal static void ValidateMessageName(string messageName)
    {
        Argument.IsNotNullOrWhiteSpace(messageName);
        _ValidateMessageName(messageName);
    }

    /// <summary>
    /// Validates message-name format and constraints.
    /// </summary>
    private static void _ValidateMessageName(string messageName)
    {
        const int maxMessageNameLength = 255;

        if (messageName.Length > maxMessageNameLength)
        {
            throw new ArgumentException(
                $"Message name '{messageName}' exceeds maximum length of {maxMessageNameLength} characters.",
                nameof(messageName)
            );
        }

        if (messageName.StartsWith('.') || messageName.EndsWith('.'))
        {
            throw new ArgumentException(
                $"Message name '{messageName}' cannot start or end with a dot.",
                nameof(messageName)
            );
        }

        if (messageName.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Message name '{messageName}' cannot contain consecutive dots.",
                nameof(messageName)
            );
        }

        foreach (var c in messageName)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_')
            {
                throw new ArgumentException(
                    $"Message name '{messageName}' contains invalid character '{c}'. Only alphanumeric characters, dots, hyphens, and underscores are allowed.",
                    nameof(messageName)
                );
            }
        }
    }

    internal ConsumerMetadata CreateConsumerMetadata(
        Type consumerType,
        Type messageType,
        string? messageName,
        string? mappedMessageName,
        string? group,
        byte concurrency,
        string? handlerId = null,
        IntentType intentType = IntentType.Bus
    )
    {
        var conventions = Conventions;
        conventions.Version = Version;

        var finalHandlerId = handlerId ?? MessagingConventions.GetDefaultHandlerId(consumerType, messageType);
        var resolvedMessageName =
            messageName ?? mappedMessageName ?? conventions.GetMessageName(messageType) ?? messageType.Name;
        var finalMessageName = ApplyMessageNamePrefix(resolvedMessageName);
        var finalGroup = ResolveGroupName(finalHandlerId, group);

        return new ConsumerMetadata(
            messageType,
            consumerType,
            finalMessageName,
            finalGroup,
            concurrency,
            intentType,
            finalHandlerId
        );
    }

    internal string ResolveGroupName(string handlerId, string? explicitGroup = null)
    {
        Argument.IsNotNullOrWhiteSpace(handlerId);

        Conventions.Version = Version;

        var resolvedGroup =
            !string.IsNullOrWhiteSpace(explicitGroup) ? explicitGroup
            : !string.IsNullOrWhiteSpace(Conventions.DefaultGroup) ? Conventions.DefaultGroup
            : IsDefaultGroupNameConfigured ? DefaultGroupName
            : Conventions.GetGroupName(handlerId);

        return ApplyGroupNamePrefix(resolvedGroup);
    }
}

internal sealed class MessagingOptionsValidator : AbstractValidator<MessagingOptions>
{
    public MessagingOptionsValidator(IMiddlewareDescriptorRegistry? middlewareDescriptorRegistry = null)
    {
        RuleFor(x => x.RetryPolicy)
            .NotNull()
            .WithMessage("RetryPolicy must not be null.")
            .SetValidator(new RetryPolicyOptionsValidator());
        RuleFor(x => x.TransportPublishTimeout)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("TransportPublishTimeout must be greater than zero.")
            .LessThanOrEqualTo(TimeSpan.FromMinutes(5))
            .WithMessage("TransportPublishTimeout must not exceed 5 minutes.");
        RuleFor(x => x.CommandTimeout)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("CommandTimeout must be greater than zero.")
            .LessThanOrEqualTo(TimeSpan.FromMinutes(5))
            .WithMessage("CommandTimeout must not exceed 5 minutes.");
        RuleFor(x => x.OutboxFlushTimeout)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("OutboxFlushTimeout must be greater than zero.")
            .LessThanOrEqualTo(TimeSpan.FromMinutes(5))
            .WithMessage("OutboxFlushTimeout must not exceed 5 minutes.");
        RuleFor(x => x.ShutdownTimeout)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("ShutdownTimeout must be greater than zero.")
            .LessThanOrEqualTo(TimeSpan.FromMinutes(5))
            .WithMessage("ShutdownTimeout must not exceed 5 minutes.");
        // No upper bound: the reconcile is a backstop cadence, not a correctness deadline (the per-row
        // LockedUntil floor recovers rows independently), so a long interval is a legitimate choice.
        RuleFor(x => x.DeadNodeReconcileInterval)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("DeadNodeReconcileInterval must be greater than zero.");
        // #2 — Version is persisted as a literal into a VARCHAR(20)/nvarchar(20) column by the SQL
        // storage providers; reject >20 chars at startup instead of failing every outbox insert at runtime.
        RuleFor(x => x.Version)
            .NotEmpty()
            .WithMessage("Version must not be empty.")
            .MaximumLength(20)
            .WithMessage("Version must not exceed 20 characters (it is stored in a VARCHAR(20) column).");
        RuleFor(x => x.SchedulerBatchSize).GreaterThan(0).WithMessage("SchedulerBatchSize must be greater than zero.");
        RuleFor(x => x.RetryBatchSize).GreaterThan(0).WithMessage("RetryBatchSize must be greater than zero.");
        RuleFor(x => x).Custom((_, _) => _ValidateMiddlewareDescriptors(middlewareDescriptorRegistry));
    }

    private static void _ValidateMiddlewareDescriptors(IMiddlewareDescriptorRegistry? registry)
    {
        if (registry is null)
        {
            return;
        }

        foreach (var descriptor in registry.Descriptors)
        {
            if (descriptor.Scope != MiddlewareScope.Bus || !_IsTypedContext(descriptor.ContextType))
            {
                continue;
            }

            throw new MessagingConfigurationException(
                $"Middleware `{descriptor.MiddlewareType.FullName}` is registered at bus scope but declares typed context `{descriptor.ContextType.FullName}`. Typed middleware must use AddConsumeMiddlewareFor<...>(group) or AddPublishMiddlewareFor<...>()."
            );
        }
    }

    private static bool _IsTypedContext(Type contextType)
    {
        return contextType.IsGenericType
            && (
                contextType.GetGenericTypeDefinition() == typeof(ConsumeContext<>)
                || contextType.GetGenericTypeDefinition() == typeof(PublishingContext<>)
            );
    }
}
