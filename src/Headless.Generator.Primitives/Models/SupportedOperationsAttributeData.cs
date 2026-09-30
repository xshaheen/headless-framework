// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Generator.Primitives.Models;

internal sealed record SupportedOperationsAttributeData
{
    /// <summary>Indicates whether addition operators should be generated.</summary>
    public bool Addition { get; init; }

    /// <summary>Indicates whether subtraction operators should be generated.</summary>
    public bool Subtraction { get; init; }

    /// <summary>Indicates whether multiplication operators should be generated.</summary>
    public bool Multiplication { get; init; }

    /// <summary>Indicates whether division operators should be generated.</summary>
    public bool Division { get; init; }

    /// <summary>Indicates whether modulus operators should be generated.</summary>
    public bool Modulus { get; init; }
}
