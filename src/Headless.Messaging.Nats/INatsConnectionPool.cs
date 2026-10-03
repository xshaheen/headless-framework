// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;

namespace Headless.Messaging.Nats;

/// <summary>
/// Manages a fixed pool of <c>NatsConnection</c> instances for the NATS JetStream transport.
/// </summary>
/// <remarks>
/// Connections are long-lived and multiplexed; they are not rented or returned. Instead,
/// <see cref="GetConnection"/> selects a connection using round-robin distribution.
/// </remarks>
internal interface INatsConnectionPool : IAsyncDisposable
{
    /// <summary>Gets the formatted NATS server addresses used by this pool.</summary>
    string ServersAddress { get; }

    /// <summary>
    /// Returns a connection from the pool using round-robin distribution. The returned connection
    /// is shared and long-lived; do not dispose it.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    NatsConnection GetConnection();
}
