// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Headless.Messaging.RabbitMq;

/// <summary>
/// Manages a pool of AMQP channels over a single shared RabbitMQ connection.
/// </summary>
/// <remarks>
/// Channels are rented for publish operations and returned after use. The pool size is fixed at
/// 15 slots by default. Renting blocks when all slots are occupied until a channel is returned.
/// </remarks>
internal interface IConnectionChannelPool
{
    /// <summary>Gets the broker host address in <c>host:port</c> form.</summary>
    string HostAddress { get; }

    /// <summary>
    /// Gets the effective exchange name, which includes the messaging version suffix when the
    /// configured version is not <c>"v1"</c>.
    /// </summary>
    string Exchange { get; }

    /// <summary>Returns the shared, lazily-established AMQP connection, opening it if necessary.</summary>
    /// <param name="cancellationToken">Token to cancel connection setup.</param>
    Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a new AMQP connection that the caller owns and disposes, with the client library's automatic recovery
    /// turned off.
    /// </summary>
    /// <remarks>
    /// An every-instance consumer holds its exclusive queue on a connection of its own: the broker deletes the queue
    /// when that connection closes, and a lost connection stays lost so the messaging core rebuilds the consumer and
    /// raises its re-established signal. Automatic topology recovery would instead re-declare the server-named queue
    /// under a new name without telling anyone.
    /// </remarks>
    /// <param name="cancellationToken">Token to cancel connection setup.</param>
    Task<IConnection> CreateNonRecoveringConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rents an AMQP channel from the pool, blocking until a slot is available.
    /// The caller must return the channel via <see cref="Return"/> when done.
    /// </summary>
    Task<IChannel> Rent();

    /// <summary>
    /// Rents an AMQP channel from the pool, blocking until a slot is available or
    /// <paramref name="cancellationToken"/> is cancelled.
    /// The caller must return the channel via <see cref="Return"/> when done.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IChannel> Rent(CancellationToken cancellationToken);

    /// <summary>
    /// Returns a previously rented channel to the pool. If the pool is full or the channel is
    /// closed, the channel is disposed instead.
    /// </summary>
    /// <param name="context">The channel to return.</param>
    /// <returns>
    /// <see langword="true"/> if the channel was returned to the pool;
    /// <see langword="false"/> if it was disposed because the pool was full or the channel was closed.
    /// </returns>
    bool Return(IChannel context);
}
