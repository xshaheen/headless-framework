// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages.Exceptions;

/// <summary>
/// Represents an error that occurs when the messaging system cannot establish or maintain a connection to the message broker.
/// This exception is thrown when connection issues prevent message publishing or consuming operations.
/// </summary>
/// <remarks>
/// Common causes include:
/// <list type="bullet">
/// <item><description>The broker service is offline or unreachable.</description></item>
/// <item><description>Network connectivity issues prevent communication with the broker.</description></item>
/// <item><description>Authentication or authorization failures when connecting to the broker.</description></item>
/// <item><description>Broker-side resource limits or configuration issues.</description></item>
/// </list>
/// When this exception occurs, the messaging system will attempt to reconnect based on configured retry policies.
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="BrokerConnectionException"/> class with a reference to the inner exception
/// that caused this connection error.
/// </remarks>
/// <param name="innerException">
/// The underlying exception that caused the connection failure.
/// This typically contains specific details about the connection error from the broker client library.
/// </param>
public class BrokerConnectionException(Exception innerException) : Exception("Broker Unreachable", innerException);
