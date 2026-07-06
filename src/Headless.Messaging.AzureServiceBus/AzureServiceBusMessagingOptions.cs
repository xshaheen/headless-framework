// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Core;
using Azure.Messaging.ServiceBus;
using FluentValidation;
using Headless.Messaging.AzureServiceBus.Producer;

namespace Headless.Messaging.AzureServiceBus;

/// <summary>
/// Configuration options for the Azure Service Bus messaging transport.
/// </summary>
/// <remarks>
/// Authentication is an either/or contract: supply either a <see cref="ConnectionString"/> or both a
/// <see cref="Namespace"/> and a <see cref="TokenCredential"/>. Both are nullable because neither is
/// universally required, and the validator enforces that exactly one authentication mode is configured.
/// Mixing both authentication modes is not supported.
/// </remarks>
public sealed class AzureServiceBusMessagingOptions
{
    /// <summary>The default topic path used for messaging (<c>"messaging"</c>).</summary>
    public const string DefaultTopicPath = "messaging";

    /// <summary>
    /// The Service Bus namespace connection string. Must target the namespace, not a specific
    /// entity. Leave <see langword="null"/> when using <see cref="TokenCredential"/> with
    /// <see cref="Namespace"/> instead. Defaults to <see langword="null"/>.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The fully-qualified Service Bus namespace hostname
    /// (for example <c>"mybus.servicebus.windows.net"</c>). Required when authenticating via
    /// <see cref="TokenCredential"/>; ignored when <see cref="ConnectionString"/> is set.
    /// Defaults to <see langword="null"/>.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// When set to <see langword="true"/> (default), topics, subscriptions, and rules are automatically created
    /// using the <see cref="Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient"/>.
    /// Set to <see langword="false"/> to skip automatic provisioning — useful when the admin API is unavailable
    /// (e.g., Azure Service Bus Emulator) or entities are managed externally (e.g., via Infrastructure as Code).
    /// </summary>
    /// <remarks>
    /// When <see langword="false"/>, all required topics, subscriptions, and subscription filter rules must already
    /// exist before the application starts.
    /// </remarks>
    public bool AutoProvision { get; set; } = true;

    /// <summary>
    /// Whether Service Bus sessions are enabled. If enabled, all messages must contain a
    /// <see cref="AzureServiceBusHeaders.SessionId" /> header. Defaults to false.
    /// </summary>
    public bool EnableSessions { get; set; }

    /// <summary>
    /// The name of the topic relative to the service namespace base address.
    /// </summary>
    public string TopicPath { get; set; } = DefaultTopicPath;

    /// <summary>
    /// The <see cref="TimeSpan" /> idle interval after which the subscription is automatically deleted.
    /// </summary>
    /// <remarks>The minimum duration is 5 minutes. Default value is <see cref="TimeSpan.MaxValue" />.</remarks>
    public TimeSpan SubscriptionAutoDeleteOnIdle { get; set; } = TimeSpan.MaxValue;

    /// <summary>
    /// Duration of a peek lock receive. i.e., the amount of time that the message is locked by a given receiver so that
    /// no other receiver receives the same message.
    /// </summary>
    /// <remarks>Max value is 5 minutes. Default value is 60 seconds.</remarks>
    public TimeSpan SubscriptionMessageLockDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The default time to live value for the messages. This is the duration after which the message expires.
    /// </summary>
    /// <remarks>
    /// This is the default value used when <see cref="ServiceBusMessage.TimeToLive"/> is not set on a
    ///  message itself. Messages older than their TimeToLive value will expire and no longer be retained in the message store.
    ///  Subscribers will be unable to receive expired messages.
    /// Default value is <see cref="TimeSpan.MaxValue"/>.
    /// </remarks>
    public TimeSpan SubscriptionDefaultMessageTimeToLive { get; set; } = TimeSpan.MaxValue;

    /// <summary>
    /// The maximum delivery count of a message before it is dead-lettered.
    /// </summary>
    /// <remarks>
    /// The delivery count is increased when a message is received in <see cref="ServiceBusReceiveMode.PeekLock"/> mode
    /// and didn't complete the message before the message lock expired.
    /// Default value is 10. Minimum value is 1.
    /// </remarks>
    public int SubscriptionMaxDeliveryCount { get; set; } = 10;

    /// <summary>
    /// Adds additional correlation properties to all correlation filters.
    /// https://learn.microsoft.com/en-us/azure/service-bus-messaging/topic-filters#correlation-filters
    /// </summary>
    public IDictionary<string, string> DefaultCorrelationHeaders { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the maximum number of concurrent calls to the ProcessMessageAsync message handler the processor should initiate.
    /// </summary>
    /// <remarks>Default values is 1.</remarks>
    public int MaxConcurrentCalls { get; set; } = 1;

    /// <summary>
    /// The maximum amount of time to wait for a message to be received for the
    ///  currently active session. After this time has elapsed, the processor will close the session
    ///  and attempt to process another session.
    /// </summary>
    /// <remarks>Not applicable when <see cref="EnableSessions"/> is false.</remarks>
    public TimeSpan? SessionIdleTimeout { get; set; }

    /// <summary>
    /// The maximum number of sessions that can be processed concurrently by the processor.
    /// </summary>
    /// <remarks>
    /// Not applicable when <see cref="EnableSessions"/> is false.
    /// The default value is 8.
    /// </remarks>
    public int MaxConcurrentSessions { get; set; } = 8;

    /// <summary>
    /// The maximum duration within which the lock will be renewed automatically.
    /// </summary>
    /// <remarks>
    /// This value should be greater than the longest message lock duration; for example, the LockDuration Property.
    /// To specify an infinite duration, use <see cref="Timeout.InfiniteTimeSpan"/>.
    /// The default value is 5 minutes.
    /// </remarks>
    public TimeSpan MaxAutoLockRenewalDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Represents the Azure Active Directory token provider for Azure Managed Service Identity integration.
    /// </summary>
    public TokenCredential? TokenCredential { get; set; }

    /// <summary>
    /// Use this function to write additional headers from the original ASB Message or any Custom Header, i.e. to allow
    /// compatibility with heterogeneous systems, into <see cref="MessageHeader" />
    /// </summary>
    public Func<
        ServiceBusReceivedMessage,
        IServiceProvider,
        List<KeyValuePair<string, string>>
    >? CustomHeadersBuilder { get; set; }

    /// <summary>
    /// Custom SQL Filters for topic subscription , more about SQL Filters and its rules
    /// Key: Rule Name , Value: SQL Expression
    /// https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-messaging-sql-filter
    /// </summary>
    public List<KeyValuePair<string, string>> SqlFilters { get; } = [];

    internal ICollection<IServiceBusProducerDescriptor> CustomProducers { get; set; } = [];

    /// <summary>
    /// Registers a custom producer for message type <typeparamref name="T"/>, directing its
    /// messages to a topic path different from the shared <see cref="TopicPath"/>. Use this when
    /// a message type must target a dedicated topic or integrate with an existing Service Bus topology.
    /// </summary>
    /// <typeparam name="T">The message type produced by the custom producer.</typeparam>
    /// <param name="configuration">A delegate that configures the producer descriptor.</param>
    /// <returns>The current options instance for chaining.</returns>
    public AzureServiceBusMessagingOptions ConfigureCustomProducer<T>(
        Action<ServiceBusProducerDescriptorBuilder<T>> configuration
    )
    {
        var builder = new ServiceBusProducerDescriptorBuilder<T>();
        configuration(builder);
        CustomProducers.Add(builder.Build());

        return this;
    }
}

internal sealed class AzureServiceBusMessagingOptionsValidator : AbstractValidator<AzureServiceBusMessagingOptions>
{
    public AzureServiceBusMessagingOptionsValidator()
    {
        RuleFor(x => x)
            .Must(x =>
                !string.IsNullOrWhiteSpace(x.ConnectionString)
                || (!string.IsNullOrWhiteSpace(x.Namespace) && x.TokenCredential is not null)
            )
            .WithMessage("Azure Service Bus requires either a ConnectionString or both Namespace and TokenCredential.");

        RuleFor(x => x.TopicPath).NotEmpty();
        RuleFor(x => x.MaxConcurrentCalls).GreaterThanOrEqualTo(1);
        RuleFor(x => x.SubscriptionMaxDeliveryCount).GreaterThanOrEqualTo(1);
    }
}
