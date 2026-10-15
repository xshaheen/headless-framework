// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http;
using System.Net.Sockets;

namespace Headless.Messaging.Transport;

/// <summary>
/// Tells a failure that could not reach the broker from one the broker answered, for the failures every transport
/// shares. A transport adds its own client's connection errors on top, and reports a match with
/// <see cref="OperateResult.BrokerUnreachable" />.
/// </summary>
/// <remarks>
/// Only a reachability failure may count toward the host's readiness: a missing topic, a rejected message, or one too
/// large is a problem with that message, and counting it would let one bad message take the host out of rotation.
/// </remarks>
internal static class BrokerFailures
{
    /// <summary>
    /// Whether <paramref name="exception" /> or any exception it wraps is a socket, I/O, HTTP transport, or timeout
    /// failure.
    /// </summary>
    public static bool IsConnectivity(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException or IOException or HttpRequestException or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }
}
