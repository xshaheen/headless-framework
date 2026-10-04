// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Sequences;

/// <summary>Represents the unique persistent identifier of a counter.</summary>
/// <remarks>
/// All components map to non-nullable primary key columns. An empty <paramref name="TenantId"/> denotes the host
/// scope, and an empty <paramref name="Partition"/> denotes an unpartitioned counter.
/// </remarks>
/// <param name="TenantId">The owning tenant identifier, or empty for the host scope.</param>
/// <param name="Name">The counter name.</param>
/// <param name="Partition">The partition key, or empty for none.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct SequenceKey(string TenantId, string Name, string Partition);
