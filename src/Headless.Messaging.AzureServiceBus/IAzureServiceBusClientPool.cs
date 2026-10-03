// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Headless.Checks;
using Headless.Messaging.AzureServiceBus.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.AzureServiceBus;

/// <summary>
/// Owns the single <see cref="ServiceBusClient"/> for the configured namespace and the pool of
/// <see cref="ServiceBusSender"/> instances keyed by entity path (topic or queue name).
/// </summary>
/// <remarks>
/// One <see cref="ServiceBusClient"/> equals one multiplexed AMQP connection; senders, processors,
/// and receivers created from it share that connection. The pool is registered as a singleton and
/// shared by the bus and queue transports and the consumer clients, so co-registering all of them
/// uses a single connection. Disposal drains every materialized sender before disposing the client;
/// consumers must stop their processors before the container disposes the pool (the bootstrapper
/// stops consumers first, so this holds under normal host shutdown).
/// </remarks>
internal interface IAzureServiceBusClientPool : IAsyncDisposable
{
    /// <summary>
    /// Gets the shared <see cref="ServiceBusSender"/> for the given entity path, creating the
    /// namespace client and the sender on first use. The returned sender is long-lived and shared;
    /// do not dispose it.
    /// </summary>
    /// <param name="entityPath">The topic or queue name the sender publishes to.</param>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    ServiceBusSender GetSender(string entityPath);

    /// <summary>
    /// Gets the shared namespace <see cref="ServiceBusClient"/>, creating it on first use.
    /// Used by consumer clients to create processors that multiplex the same AMQP connection as
    /// the publish senders. The returned client is shared and pool-owned; do not dispose it.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    ServiceBusClient GetClient();

    /// <summary>
    /// Gets the shared <see cref="ServiceBusAdministrationClient"/> for topology provisioning,
    /// creating it on first use. The administration client is HTTP-based and holds no connection;
    /// it is shared for the same single-instance-per-namespace reason as the messaging client.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    ServiceBusAdministrationClient GetAdministrationClient();
}
