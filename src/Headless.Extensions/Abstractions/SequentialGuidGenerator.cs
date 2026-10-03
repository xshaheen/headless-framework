// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;

namespace Headless.Abstractions;

/// <summary>
/// <see cref="IGuidGenerator"/> whose ordering strategy is chosen per backend via <see cref="SequentialGuidType"/>.
/// Stateless and safe to register as a singleton (keyed by the strategy for per-backend resolution).
/// </summary>
public sealed class SequentialGuidGenerator(SequentialGuidType type) : IGuidGenerator
{
    /// <summary>Creates a new sequential <see cref="Guid"/> using the configured <see cref="SequentialGuidType"/>.</summary>
    /// <returns>A newly generated sequential <see cref="Guid"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the configured <see cref="SequentialGuidType"/> is not a recognized value.</exception>
    public Guid Create()
    {
        return type switch
        {
            SequentialGuidType.Version7 => Guid.CreateVersion7(),
            SequentialGuidType.SqlServer => SequentialGuid.NextSequentialAtEnd(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, message: null),
        };
    }
}
