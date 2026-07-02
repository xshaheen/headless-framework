// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>
/// Thrown when an operation requires an ambient tenant context but none is available
/// (neither set via <see cref="ICurrentTenant.Change"/> nor provided explicitly by
/// the caller). Cross-layer guard exception shared by every Headless component that
/// enforces a "tenant must be resolved" invariant.
/// </summary>
/// <remarks>
/// Inherits from <see cref="Exception"/> directly so cross-cutting middleware (HTTP problem mappers,
/// retry suppression) can catch this single type without sweeping unrelated
/// <see cref="InvalidOperationException"/>s.
/// </remarks>
[PublicAPI]
public sealed class MissingTenantContextException : Exception
{
    /// <summary>
    /// Initializes a new instance of <see cref="MissingTenantContextException"/> with the
    /// default diagnostic message that explains how to resolve the missing context.
    /// </summary>
    public MissingTenantContextException()
        : base(_DefaultMessage) { }

    /// <summary>
    /// Initializes a new instance of <see cref="MissingTenantContextException"/> with a
    /// custom message.
    /// </summary>
    /// <param name="message">A message that describes the missing tenant context condition.</param>
    public MissingTenantContextException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance of <see cref="MissingTenantContextException"/> with a
    /// custom message and an inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">A message that describes the missing tenant context condition.</param>
    /// <param name="innerException">The exception that caused this exception, or <see langword="null"/>.</param>
    public MissingTenantContextException(string message, Exception? innerException)
        : base(message, innerException) { }

    private const string _DefaultMessage =
        "An operation required an ambient tenant context but none was set. "
        + "Wrap the call in ICurrentTenant.Change(tenantId) to scope the AsyncLocal "
        + "accessor, or pass the tenant identifier explicitly through the operation's options.";
}
