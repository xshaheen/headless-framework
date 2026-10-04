// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Generator.Primitives;

/// <summary>Identifies the underlying primitive storage type for an attributed class or struct.</summary>
/// <param name="underlyingPrimitiveType">The underlying primitive type.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class UnderlyingPrimitiveTypeAttribute(Type underlyingPrimitiveType) : Attribute
{
    /// <summary>Gets the underlying primitive type.</summary>
    public Type UnderlyingPrimitiveType { get; } = underlyingPrimitiveType;
}
