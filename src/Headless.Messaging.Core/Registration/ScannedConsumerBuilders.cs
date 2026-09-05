// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;

namespace Headless.Messaging.Registration;

/// <summary>
/// Describes a consumer discovered by assembly scanning before it is registered.
/// </summary>
[PublicAPI]
public sealed record ScannedConsumerContext
{
    public ScannedConsumerContext(Type consumerType, Type messageType)
    {
        Argument.IsNotNull(consumerType);
        Argument.IsNotNull(messageType);

        ConsumerType = consumerType;
        MessageType = messageType;
    }

    /// <summary>The concrete consumer implementation type discovered by scanning.</summary>
    public Type ConsumerType { get; }

    /// <summary>The closed message type consumed by <see cref="ConsumerType"/>.</summary>
    public Type MessageType { get; }
}

/// <summary>
/// Configures a consumer discovered by assembly scanning.
/// </summary>
[PublicAPI]
public interface IScannedConsumerBuilder
{
    /// <summary>Sets the consumer group name for this scanned consumer registration.</summary>
    /// <param name="group">A non-whitespace group name (Kafka group.id or RabbitMQ queue name).</param>
    /// <returns>The same builder instance for chaining.</returns>
    IScannedConsumerBuilder Group(string group);

    /// <summary>Limits the number of messages consumed concurrently by this scanned consumer.</summary>
    /// <param name="maxConcurrent">Maximum concurrent deliveries; must be greater than zero.</param>
    /// <returns>The same builder instance for chaining.</returns>
    IScannedConsumerBuilder Concurrency(byte maxConcurrent);

    /// <summary>Overrides the deterministic handler identity for diagnostics and default group generation.</summary>
    /// <param name="handlerId">An explicit handler identity string; this is not the durable inbox identity.</param>
    /// <returns>The same builder instance for chaining.</returns>
    IScannedConsumerBuilder HandlerId(string handlerId);

    /// <summary>Sets the operator-stable identity used by the durable inbox.</summary>
    /// <param name="consumerIdentity">Nonblank identity of at most <see cref="ConsumerMetadata.ConsumerIdentityMaxLength"/> characters that remains unchanged across handler and topology refactors.</param>
    /// <returns>The same builder instance for chaining.</returns>
    IScannedConsumerBuilder ConsumerIdentity(string consumerIdentity);

    /// <summary>Defines the stable logical name and schema version of the discovered message contract.</summary>
    /// <param name="name">The stable logical contract name.</param>
    /// <param name="version">The schema version. Defaults to the initial version.</param>
    /// <returns>The same builder instance for chaining.</returns>
    IScannedConsumerBuilder Contract(string name, string version = MessageOptions.InitialContractVersion);

    /// <summary>Overrides the terminal inbox retention captured for future generations.</summary>
    /// <param name="retention">A positive whole-second duration no greater than <see cref="int.MaxValue"/> seconds.</param>
    /// <returns>The same builder instance for chaining.</returns>
    IScannedConsumerBuilder InboxRetention(TimeSpan retention);

    /// <summary>Configures per-consumer circuit breaker overrides for this scanned registration.</summary>
    /// <param name="configure">A callback that mutates a <see cref="ConsumerCircuitBreakerOptions"/> instance for this consumer.</param>
    /// <returns>The same builder instance for chaining.</returns>
    IScannedConsumerBuilder WithCircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure);

    /// <summary>
    /// Excludes this scanned consumer from both message registration and dependency injection.
    /// </summary>
    /// <returns>The same builder instance for chaining.</returns>
    IScannedConsumerBuilder Skip();
}

internal sealed class ScannedConsumerBuilder(Type consumerType, MessageLane lane) : IScannedConsumerBuilder
{
    private readonly MessageConsumerRegistrationBuilder _registration = new(consumerType, lane, isAssemblyScan: true);

    public bool IsSkipped { get; private set; }

    public string? MessageName { get; private set; }

    public string ContractVersion { get; private set; } = MessageOptions.InitialContractVersion;

    public IScannedConsumerBuilder Group(string group)
    {
        _registration.SetGroup(group);
        return this;
    }

    public IScannedConsumerBuilder Concurrency(byte maxConcurrent)
    {
        _registration.SetConcurrency(maxConcurrent);
        return this;
    }

    public IScannedConsumerBuilder HandlerId(string handlerId)
    {
        _registration.SetHandlerId(handlerId);
        return this;
    }

    public IScannedConsumerBuilder ConsumerIdentity(string consumerIdentity)
    {
        _registration.SetConsumerIdentity(consumerIdentity);
        return this;
    }

    public IScannedConsumerBuilder Contract(string name, string version = MessageOptions.InitialContractVersion)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        MessagingOptions.ValidateContractVersion(version);
        MessageName = name;
        ContractVersion = version;
        return this;
    }

    public IScannedConsumerBuilder InboxRetention(TimeSpan retention)
    {
        _registration.SetInboxRetention(retention);
        return this;
    }

    public IScannedConsumerBuilder WithCircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure)
    {
        _registration.SetCircuitBreaker(configure);
        return this;
    }

    public IScannedConsumerBuilder Skip()
    {
        IsSkipped = true;
        return this;
    }

    public MessageConsumerRegistration Build()
    {
        return _registration.Build();
    }
}
