// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headless.AuditLog;

/// <summary>
/// Fills the columns an application added to the audit log table. Entity Framework storage calls every registered
/// enricher once for each audit row, when the row is added to its <c>DbContext</c> and before it is saved.
/// </summary>
/// <remarks>
/// <para>
/// Add a column by mapping a property on <see cref="AuditLogEntry"/> after
/// <c>modelBuilder.ConfigureHeadlessAuditLog(...)</c>, for example
/// <c>modelBuilder.Entity&lt;AuditLogEntry&gt;().Property&lt;string&gt;("Category")</c>, and set it here with
/// <c>entry.Property("Category").CurrentValue = ...</c>. The row's own fields are set already, so an enricher can
/// derive a value from <see cref="AuditLogEntry.Action"/> or <see cref="AuditLogEntry.EntityType"/>; the
/// <see cref="AuditLogEntry.EntityId"/> of an entity whose key the database generates is not known yet.
/// </para>
/// <para>
/// It runs for change-capture rows, for <see cref="IAuditLog{TContext}"/>, and for
/// <see cref="IAuditLogWriter{TContext}"/>. Register implementations in the container at any lifetime; they run in
/// registration order. An exception fails the write that produced the row.
/// </para>
/// </remarks>
[PublicAPI]
public interface IAuditLogEntryEnricher
{
    /// <summary>Sets the application's own columns on <paramref name="entry"/>.</summary>
    /// <param name="entry">The tracked audit row, in the <c>Added</c> state.</param>
    void Enrich(EntityEntry<AuditLogEntry> entry);
}
