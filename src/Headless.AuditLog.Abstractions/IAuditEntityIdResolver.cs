// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Resolves deferred store-generated values on captured audit entries after SaveChanges has
/// assigned real keys. Implemented by capture services that support two-phase resolution.
/// </summary>
public interface IAuditEntityIdResolver
{
    /// <summary>
    /// Patches <see cref="AuditLogEntryData.EntityId"/> on entries whose primary key was
    /// deferred (e.g., identity/sequence-backed Added entities), and replaces captured property
    /// values that held provider temporary values (store-generated keys, foreign keys pointing
    /// at just-added principals) with the real post-save values. Call after SaveChanges.
    /// </summary>
    /// <remarks>
    /// Implementations MUST resolve only the entries passed in — never state shared with other
    /// in-flight captures — and MUST be safe to call again for the same entries after an
    /// execution-strategy retry, where store-generated keys may differ between attempts.
    /// </remarks>
    void ResolveEntityIds(IReadOnlyList<AuditLogEntryData> entries);

    /// <summary>
    /// Reports whether <paramref name="entry"/> still needs <see cref="ResolveEntityIds"/> after SaveChanges. An entry
    /// that returns <see langword="false"/> is complete as captured, so a store that adds rows to the saving context
    /// can persist it in the same batch as the entity change instead of in a second save.
    /// </summary>
    /// <remarks>The default is <see langword="true"/>, which keeps every entry in the two-phase flow.</remarks>
    bool RequiresDeferredResolution(AuditLogEntryData entry) => true;
}
