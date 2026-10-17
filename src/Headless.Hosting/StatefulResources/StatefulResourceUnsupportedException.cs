// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>
/// Thrown on purpose by a stateful resource, or a transport topology provisioner, for an operation it cannot express,
/// such as clearing a resource that holds no data the broker can remove. Runners report it as unsupported instead of
/// failed; any other <see cref="NotSupportedException"/> from inside an operation, a driver's included, is a failure.
/// </summary>
[PublicAPI]
public sealed class StatefulResourceUnsupportedException(string? message, Exception? innerException = null)
    : NotSupportedException(message, innerException);
