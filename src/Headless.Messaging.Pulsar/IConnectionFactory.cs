// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pulsar.Client.Api;

namespace Headless.Messaging.Pulsar;

/// <summary>
/// Manages the shared Pulsar client and per-topic producer cache for the Pulsar transport.
/// </summary>
/// <remarks>
/// The <c>PulsarClient</c> is created lazily on the first call to <see cref="RentClientAsync"/> or
/// <see cref="CreateProducerAsync"/>. Producers are cached per topic; a failed producer task is
/// evicted from the cache so the next call creates a fresh producer.
/// </remarks>
internal interface IConnectionFactory
{
    /// <summary>Gets the formatted Pulsar service URL used by this factory.</summary>
    string ServersAddress { get; }

    /// <summary>
    /// Returns a cached producer for <paramref name="topic"/>, creating one on first call.
    /// A failed producer is evicted from the cache so the next call can retry.
    /// </summary>
    /// <param name="topic">The fully-qualified Pulsar topic name.</param>
    Task<IProducer<byte[]>> CreateProducerAsync(string topic);

    /// <summary>
    /// Returns the shared <c>PulsarClient</c>, creating it if it has not been opened yet.
    /// The client is long-lived; do not dispose it directly.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel client acquisition or creation.</param>
    Task<PulsarClient> RentClientAsync(CancellationToken cancellationToken = default);
}
