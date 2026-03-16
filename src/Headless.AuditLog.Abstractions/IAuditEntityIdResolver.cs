// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Resolves deferred entity IDs on captured audit entries after SaveChanges has assigned
/// store-generated keys. Implemented by capture services that support two-phase ID resolution.
/// </summary>
public interface IAuditEntityIdResolver
{
    /// <summary>
    /// Patches <see cref="AuditLogEntryData.EntityId"/> on entries whose primary key was
    /// deferred (e.g., identity/sequence-backed Added entities). Call after SaveChanges.
    /// </summary>
    void ResolveEntityIds(IReadOnlyList<AuditLogEntryData> entries);
}
