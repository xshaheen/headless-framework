// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;

namespace Headless.Abstractions;

/// <summary>Abstraction over <see cref="Guid"/> creation, allowing the ordering strategy to vary per implementation.</summary>
public interface IGuidGenerator
{
    /// <summary>Creates a new <see cref="Guid"/>.</summary>
    /// <returns>A newly generated <see cref="Guid"/>.</returns>
    Guid Create();
}
