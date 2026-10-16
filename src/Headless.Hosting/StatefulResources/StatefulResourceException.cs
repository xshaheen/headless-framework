// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>
/// Thrown when a stateful resource is not set up as expected, or when one or more resource operations failed.
/// </summary>
[PublicAPI]
public sealed class StatefulResourceException(string? message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    /// <summary>The failures that make up this exception, when it reports several operations; otherwise empty.</summary>
    public IReadOnlyList<Exception> Failures { get; init; } = [];
}
