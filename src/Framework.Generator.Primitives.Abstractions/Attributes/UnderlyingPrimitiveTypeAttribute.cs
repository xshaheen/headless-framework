// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Generator.Primitives;

/// <summary>Specifies that the attributed class or struct has an underlying primitive type.</summary>
/// <param name="underlyingPrimitiveType">The underlying primitive type.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class UnderlyingPrimitiveTypeAttribute(Type underlyingPrimitiveType) : Attribute
{
    /// <summary>Gets the underlying primitive type.</summary>
    public Type UnderlyingPrimitiveType { get; } = underlyingPrimitiveType;
}
