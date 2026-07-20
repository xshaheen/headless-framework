// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>Marks an entity that exposes a binary ETag for HTTP-level and storage-level concurrency control.</summary>
/// <remarks>
/// The ETag is typically a row-version byte sequence produced by the database. Callers may expose it as a
/// Base64-encoded value in HTTP response headers and compare it against <c>If-Match</c> or <c>If-None-Match</c>
/// request headers to implement conditional updates.
/// </remarks>
[PublicAPI]
public interface IHasETag
{
    /// <summary>Raw version token of the entity; <see langword="null"/> until the entity has been persisted.</summary>
    /// <remarks>
    /// Unlike <see cref="IHasConcurrencyStamp.ConcurrencyStamp"/> (application-generated, get-only), the ETag
    /// is written by the store: the persistence layer maps it as a database row version
    /// (for example <c>IsRowVersion()</c> in EF Core) and materializes / refreshes the value on every load and
    /// save. The setter exists for that store-side write path — domain code should treat the value as read-only.
    /// </remarks>
    byte[]? ETag { get; set; }
}
