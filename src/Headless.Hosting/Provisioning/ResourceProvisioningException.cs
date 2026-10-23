// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>
/// Thrown when a resource is not provisioned as expected, or when one or more provisioning operations failed.
/// </summary>
[PublicAPI]
public sealed class ResourceProvisioningException(string? message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    /// <summary>The failures that make up this exception, when it reports several operations; otherwise empty.</summary>
    public IReadOnlyList<Exception> Failures { get; init; } = [];
}
