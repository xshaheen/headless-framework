// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;
using StackExchange.Redis;

namespace Headless.Messaging.RedisStreams;

public class MessagingRedisOptions
{
    /// <summary>
    /// Gets or sets the native options of StackExchange.Redis
    /// </summary>
    public ConfigurationOptions? Configuration { get; set; }

    // BrokerAddress is emitted to telemetry and dashboards, so expose only broker endpoints here.
    internal string DisplayEndpoint =>
        Configuration?.EndPoints.Count > 0
            ? string.Join(",", Configuration.EndPoints.Select(BrokerAddressDisplay.GetDisplayEndpoint))
            : string.Empty;

    /// <summary>
    /// Gets or sets the count of entries consumed from stream
    /// </summary>
    public uint StreamEntriesCount { get; set; }

    /// <summary>
    /// Gets or sets the number of connections that can be used with redis server
    /// </summary>
    public uint ConnectionPoolSize { get; set; }

    /// <summary>
    /// Callback function that will be invoked when an error occurred during message consumption.
    /// </summary>
    public Func<ConsumeErrorContext, Task>? OnConsumeError { get; set; }

    public record ConsumeErrorContext(Exception Exception, StreamEntry? Entry);
}
