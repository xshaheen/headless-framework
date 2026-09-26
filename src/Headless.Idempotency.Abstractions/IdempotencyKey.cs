// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Idempotency;

/// <summary>The identity of one idempotent operation: the tenant it belongs to and its idempotency key.</summary>
/// <remarks>
/// The tenant is captured from the current tenant when the key is admitted; later calls on the admission use it, not
/// the tenant current at that time, because the record belongs to the identity it was admitted under.
/// </remarks>
/// <param name="TenantId">The owning tenant, or <see langword="null" /> for the host scope.</param>
/// <param name="Key">The caller-supplied idempotency key.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct IdempotencyKey(string? TenantId, string Key);
