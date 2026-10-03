// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Confluent.Kafka;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Kafka;

/// <summary>
/// Manages a pool of Kafka producers for publish operations.
/// </summary>
/// <remarks>
/// Producers are not thread-safe for concurrent sends; renting a producer from the pool ensures
/// exclusive access. Call <see cref="Return"/> after each publish to recycle the producer.
/// When the pool is at capacity, <see cref="Return"/> disposes the surplus producer instead.
/// </remarks>
internal interface IKafkaConnectionPool
{
    /// <summary>Gets the formatted broker addresses used by this pool.</summary>
    string ServersAddress { get; }

    /// <summary>
    /// Takes a producer from the pool, or creates a new one when the pool is empty.
    /// The caller must return the producer via <see cref="Return"/> when the publish is complete.
    /// </summary>
    IProducer<string, byte[]> RentProducer();

    /// <summary>
    /// Returns a producer to the pool. If the pool is already at its maximum size,
    /// the producer is disposed instead.
    /// </summary>
    /// <param name="producer">The producer to return or dispose.</param>
    /// <returns>
    /// <see langword="true"/> if the producer was returned to the pool;
    /// <see langword="false"/> if it was disposed because the pool was full.
    /// </returns>
    bool Return(IProducer<string, byte[]> producer);
}
