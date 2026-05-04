// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>
/// Thrown when an operation requires an ambient tenant context but none is available
/// (neither set via <see cref="ICurrentTenant.Change"/> nor provided explicitly by
/// the caller). Cross-layer guard exception shared by every Headless component that
/// enforces a "tenant must be resolved" invariant.
/// </summary>
/// <remarks>
/// Each call site enriches <see cref="System.Exception.Data"/> with a layer-specific
/// failure code (e.g., <c>"Headless.Messaging.FailureCode" = "MissingTenantContext"</c>)
/// so log aggregators can group failures per layer. Inherits from <see cref="Exception"/>
/// directly so cross-cutting middleware (HTTP 400 mappers, retry suppression) can catch
/// this single type without sweeping unrelated <see cref="InvalidOperationException"/>s.
/// </remarks>
public sealed class MissingTenantContextException : Exception
{
    public MissingTenantContextException()
        : base(_DefaultMessage) { }

    public MissingTenantContextException(string message)
        : base(message) { }

    public MissingTenantContextException(string message, Exception? innerException)
        : base(message, innerException) { }

    private const string _DefaultMessage =
        "An operation required an ambient tenant context but none was set. "
        + "Wrap the call in ICurrentTenant.Change(tenantId) to scope the AsyncLocal "
        + "accessor, or pass the tenant identifier explicitly through the operation's options.";
}
