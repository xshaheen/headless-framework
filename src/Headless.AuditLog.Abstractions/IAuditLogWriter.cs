// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Records explicit audit events that must persist on their own, such as an authorization denial or a read
/// that ends a request without saving anything. Each call commits its entry immediately in a separate
/// transaction.
/// </summary>
/// <typeparam name="TContext">
/// The persistence context type that owns the audit log table, matching <see cref="IAuditLog{TContext}"/>.
/// </typeparam>
/// <remarks>
/// Use <see cref="IAuditLog{TContext}"/> when the entry must commit or roll back with the caller's entity
/// changes. An entry written here survives a later rollback of the caller's work, and it is lost when the
/// caller relies on a <c>SaveChanges</c> that never runs.
/// </remarks>
public interface IAuditLogWriter<TContext>
{
    /// <summary>
    /// Records an explicit audit event and commits it before returning. Does nothing when
    /// <see cref="AuditLogOptions.IsEnabled"/> is <see langword="false"/>.
    /// </summary>
    /// <param name="request">The action and optional event metadata to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default);
}
