// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;

namespace Headless.Abstractions;

public interface IGuidGenerator
{
    /// <summary>Creates a new <see cref="Guid"/>.</summary>
    Guid Create();
}

/// <summary>
/// Selects how a generated <see cref="Guid"/> is ordered, so it stays sequential in the target database's
/// index sort order. The right value depends on the backend a key is persisted into, not on a global preference.
/// </summary>
public enum SequentialGuidType
{
    /// <summary>
    /// RFC 9562 UUIDv7 via <see cref="Guid.CreateVersion7()"/> — time-ordered in standard big-endian byte order.
    /// Sequential for byte-ordered stores (PostgreSQL <c>uuid</c>, MySQL binary, Oracle) and the right default for
    /// general, backend-agnostic use. NOT sequential in SQL Server's <c>uniqueidentifier</c> sort order.
    /// </summary>
    Version7,

    /// <summary>
    /// Sequential in SQL Server's <c>uniqueidentifier</c> sort order (the EF Core comb — see
    /// <see cref="SequentialGuid.NextSequentialAtEnd"/>). Use only for SQL Server clustered/primary keys, where
    /// <see cref="Version7"/> fragments because the timestamp lands in the bytes SQL Server sorts last.
    /// </summary>
    SqlServer,
}

/// <summary>
/// <see cref="IGuidGenerator"/> whose ordering strategy is chosen per backend via <see cref="SequentialGuidType"/>.
/// Stateless and safe to register as a singleton (keyed by the strategy for per-backend resolution).
/// </summary>
public sealed class SequentialGuidGenerator(SequentialGuidType type) : IGuidGenerator
{
    public Guid Create() =>
        type switch
        {
            SequentialGuidType.Version7 => Guid.CreateVersion7(),
            SequentialGuidType.SqlServer => SequentialGuid.NextSequentialAtEnd(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, message: null),
        };
}
