// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Sequences;

/// <summary>Represents the stored identity of one counter: one row per key.</summary>
/// <remarks>
/// Every part is non-null because every part is a primary-key column. The host scope (no current tenant) is
/// stored as an empty <see cref="TenantId"/>, which no real tenant id can be, and "no partition" as an empty
/// <see cref="Partition"/>. Keys are built by the sequence services after validation; providers store them
/// as given and compare them ordinally.
/// </remarks>
/// <param name="TenantId">The owning tenant id, or empty for the host scope.</param>
/// <param name="Name">The counter name.</param>
/// <param name="Partition">The partition, or empty for none.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct SequenceKey(string TenantId, string Name, string Partition);
