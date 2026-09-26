// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Idempotency;

/// <summary>The stored identity of one idempotency record: one row per key.</summary>
/// <remarks>
/// Both parts are non-null because both are primary-key columns. The host scope (no current tenant) is stored as an
/// empty <paramref name="TenantId" />, which no real tenant id can be. Keys are built by the idempotency services
/// after validation; providers store them as given and compare them ordinally.
/// </remarks>
/// <param name="TenantId">The owning tenant, or empty for the host scope.</param>
/// <param name="Key">The idempotency key.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct IdempotencyRecordKey(string TenantId, string Key)
{
    /// <summary>Gets the public tenant id: <see langword="null" /> for the host scope.</summary>
    public string? PublicTenantId => TenantId.Length == 0 ? null : TenantId;

    /// <summary>Returns the public identity this stored key carries.</summary>
    /// <returns>The key, with the host scope mapped back to a <see langword="null" /> tenant.</returns>
    public IdempotencyKey ToKey()
    {
        return new IdempotencyKey(PublicTenantId, Key);
    }
}
