// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.Checks;
using Framework.Messages.Messages;

namespace Framework.Messages.Configuration;

/// <summary>
/// Provides options to customize various aspects of the message processing pipeline. This includes settings for message expiration,
/// retry mechanisms, concurrency management, and serialization, among others. This class allows fine-tuning
/// CAP's behavior to better align with specific application requirements, such as adjusting threading models for
/// subscriber message processing, setting message expiry times, and customizing serialization settings.
/// </summary>
public class CapOptions
{
    internal IList<IMessagesOptionsExtension> Extensions { get; } = new List<IMessagesOptionsExtension>();

    /// <summary>
    /// Gets or sets the default consumer group name for subscribers.
    /// In Kafka, this corresponds to the consumer group name; in RabbitMQ, it corresponds to the queue name.
    /// Default value is "cap.queue." followed by the entry assembly name in lowercase.
    /// </summary>
    public string DefaultGroupName { get; set; } =
        "cap.queue." + Assembly.GetEntryAssembly()?.GetName().Name!.ToLower();

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
    public bool EnableSubscriberParallelExecute { get; set; } = false;

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
    public bool EnablePublishParallelSend { get; set; } = false;

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
    /// Registers a CAP options extension that will be executed when configuring CAP services.
    /// Extensions allow third-party libraries to customize CAP's behavior without modifying core configuration.
    /// </summary>
    /// <param name="extension">The extension instance to register.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="extension"/> is null.</exception>
    public void RegisterExtension(IMessagesOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
