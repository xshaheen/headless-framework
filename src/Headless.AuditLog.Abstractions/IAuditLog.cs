// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Explicit audit logging for non-mutation events such as data reads,
/// PII reveals, cross-tenant access, and other business-significant actions.
/// </summary>
public interface IAuditLog
{
    /// <summary>
    /// Records an explicit audit event. The entry is added to the current
    /// DbContext and persists with the next <c>SaveChanges</c> call.
    /// </summary>
    /// <param name="action">A dot-namespaced action name (e.g., <c>"pii.revealed"</c>).</param>
    /// <param name="entityType">Optional CLR type name of the related entity.</param>
    /// <param name="entityId">Optional string representation of the entity's primary key.</param>
    /// <param name="data">Optional payload stored in <c>NewValues</c>.</param>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="errorCode">Error code when <paramref name="success"/> is <c>false</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogAsync(
        string action,
        string? entityType = null,
        string? entityId = null,
        Dictionary<string, object?>? data = null,
        bool success = true,
        string? errorCode = null,
        CancellationToken cancellationToken = default
    );
}
