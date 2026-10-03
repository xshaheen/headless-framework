// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;

namespace Headless.Abstractions;

/// <summary>
/// Selects how a generated <see cref="Guid"/> is ordered, so it stays sequential in the target database's
/// index sort order. The right value depends on the backend a key is persisted into, not on a global preference.
/// </summary>
/// <remarks>
/// The backing values are part of the public contract and must remain stable across versions; assign new members
/// explicit values rather than reordering existing ones.
/// </remarks>
public enum SequentialGuidType
{
    /// <summary>
    /// RFC 9562 UUIDv7 via <see cref="Guid.CreateVersion7()"/> — time-ordered in standard big-endian byte order.
    /// Sequential for byte-ordered stores (PostgreSQL <c>uuid</c>, MySQL binary, Oracle) and the right default for
    /// general, backend-agnostic use. NOT sequential in SQL Server's <c>uniqueidentifier</c> sort order.
    /// </summary>
    Version7 = 0,

    /// <summary>
    /// Sequential in SQL Server's <c>uniqueidentifier</c> sort order (the EF Core comb — see
    /// <see cref="SequentialGuid.NextSequentialAtEnd"/>). Use only for SQL Server clustered/primary keys, where
    /// <see cref="Version7"/> fragments because the timestamp lands in the bytes SQL Server sorts last.
    /// </summary>
    SqlServer = 1,
}
