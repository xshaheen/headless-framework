// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Generator.Primitives;

/// <summary>Specifies supported mathematical operations for generated numeric primitive types.</summary>
/// <remarks>
/// Constrains supported operators for numeric primitives. Types such as <see cref="byte"/>,
/// <see cref="sbyte"/>, <see cref="ushort"/>, and <see cref="short"/> are excluded from these operators.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class SupportedOperationsAttribute : Attribute
{
    /// <summary>Gets or sets a value indicating whether addition operators should be generated.</summary>
    public bool Addition { get; set; }

    /// <summary>Gets or sets a value indicating whether subtraction operators should be generated.</summary>
    public bool Subtraction { get; set; }

    /// <summary>Gets or sets a value indicating whether multiplication operators should be generated.</summary>
    public bool Multiplication { get; set; }

    /// <summary>Gets or sets a value indicating whether division operators should be generated.</summary>
    public bool Division { get; set; }

    /// <summary>Gets or sets a value indicating whether modulus operators should be generated.</summary>
    public bool Modulus { get; set; }
}
