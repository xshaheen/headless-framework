// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Generator.Primitives;

/// <summary>
/// Attribute used to specify supported mathematical operations for numeric types.
/// If not provided, default values will be used for operations.
/// </summary>
/// <remarks>
/// This attribute defines supported mathematical operations for numeric types,
/// with constraints excluding <see cref="byte"/>, <see cref="sbyte"/>, <see cref="ushort"/>, and <see cref="short"/> types for the type parameter T.
/// Its usage, particularly the constraint, depends on the IPrimitive.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class SupportedOperationsAttribute : Attribute
{
    /// <summary>
    /// Indicates whether addition operators should be generated.
    /// </summary>
    public bool Addition { get; set; }

    /// <summary>
    /// Indicates whether subtraction operators should be generated.
    /// </summary>
    public bool Subtraction { get; set; }

    /// <summary>
    /// Indicates whether multiplication operators should be generated.
    /// </summary>
    public bool Multiplication { get; set; }

    /// <summary>
    /// Indicates whether division operators should be generated.
    /// </summary>
    public bool Division { get; set; }

    /// <summary>
    /// Indicates whether modulus operators should be generated.
    /// </summary>
    public bool Modulus { get; set; }
}
