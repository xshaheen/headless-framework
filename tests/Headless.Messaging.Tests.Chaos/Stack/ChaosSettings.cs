// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Reliability;
using Tests.Ledger;

namespace Tests.Stack;

/// <summary>How every host of one scenario run is configured. A scenario changes the defaults it needs before it starts.</summary>
public sealed class ChaosSettings
{
    /// <summary>Gets or sets whether hosts reach the broker through the Toxiproxy route, so a scenario can break it.</summary>
    public bool BrokerThroughProxy { get; set; }

    /// <summary>Gets or sets whether hosts reach the database through the Toxiproxy route, so a scenario can break it.</summary>
    public bool StorageThroughProxy { get; set; }

    /// <summary>Gets or sets how many work messages one host handles at once.</summary>
    public byte WorkConcurrency { get; set; } = 8;

    /// <summary>Gets or sets how many broker consumer clients each host opens.</summary>
    public int ClientsPerHost { get; set; } = 1;

    public int RetryBatchSize { get; set; } = 200;

    /// <summary>Gets or sets the messaging shutdown budget: how long a stopping host drains before it cancels.</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan DelayedPollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the work consumer's failure policy; the default rides out every outage a scenario makes.</summary>
    public Action<FailurePolicyBuilder> WorkFailurePolicy { get; set; } =
        policy => policy.Immediate(1).Delayed(20, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

    /// <summary>Gets or sets how many times the broker redelivers a message before it dead-letters it.</summary>
    public int DeliveryLimit { get; set; } = 5;

    /// <summary>Gets or sets a change to the work consumer's circuit breaker; the host default applies without one.</summary>
    public Action<ConsumerCircuitBreakerOptions>? WorkCircuitBreaker { get; set; }

    /// <summary>Gets or sets whether hosts join a cluster on the database's coordination provider.</summary>
    public bool Coordination { get; set; }

    /// <summary>Gets the sends the run breaks on purpose; a real scenario leaves it empty.</summary>
    public TransportSabotage Sabotage { get; } = new();

    /// <summary>Gets or sets a last change to each host's messaging setup.</summary>
    public Action<MessagingSetupBuilder>? ConfigureMessaging { get; set; }
}
