// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;

namespace Headless.Messaging;

/// <summary>
/// Broker-side transport that dispatches messages with point-to-point (work-queue) semantics.
/// </summary>
/// <remarks>
/// <para>
/// Each <see cref="IQueueTransport"/> implementation maps the framework-side queue intent to a
/// broker-native point-to-point primitive — RabbitMQ direct exchange, NATS queue groups, Azure
/// Service Bus queue, AWS SQS, Kafka partition, Redis Streams consumer group, Pulsar
/// shared/key-shared subscription, etc.
/// </para>
/// <para>
/// Providers that cannot natively support work-queue delivery do not implement this interface,
/// and their NuGet packages do not reference <c>Headless.Messaging.Queue.Abstractions</c>.
/// </para>
/// <para>
/// Capability is therefore declared at the package boundary: if your application registers
/// <see cref="IQueue"/> or <see cref="IOutboxQueue"/>, the host must also register at least one
/// provider that ships an <see cref="IQueueTransport"/>. Misconfigurations are caught at host startup.
/// </para>
/// </remarks>
[PublicAPI]
public interface IQueueTransport : ITransport;
