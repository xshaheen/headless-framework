// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Sockets;
using Headless.Messaging.Exceptions;

namespace Headless.Messaging.CircuitBreaker;

/// <summary>
/// Provides default predicates and constants for circuit breaker behavior.
/// </summary>
public static class CircuitBreakerDefaults
{
    /// <summary>
    /// Determines whether an exception is transient and should count toward the circuit breaker failure threshold.
    /// </summary>
    /// <remarks>
    /// Transient exceptions indicate infrastructure or connectivity problems with the dependency (broker, network, etc.)
    /// and are appropriate signals to open the circuit. Non-transient exceptions such as deserialization errors or
    /// message validation failures indicate problems with the message itself and should not trip the circuit breaker,
    /// since the dependency is likely healthy.
    /// </remarks>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    /// <see langword="true"/> if the exception is transient and should contribute to circuit breaker trips;
    /// <see langword="false"/> if the exception is permanent and the circuit breaker should ignore it.
    /// </returns>
    public static bool IsTransient(Exception exception) =>
        exception switch
        {
            TimeoutException => true,
            HttpRequestException { StatusCode: >= System.Net.HttpStatusCode.InternalServerError } => true,
            HttpRequestException { InnerException: SocketException } => true,
            SocketException => true,
            BrokerConnectionException => true,
            TaskCanceledException tce when !tce.CancellationToken.IsCancellationRequested => true,
            _ => false,
        };
}
