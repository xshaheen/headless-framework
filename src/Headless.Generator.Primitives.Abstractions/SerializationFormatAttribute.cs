// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Generator.Primitives;

/// <summary>Specifies the serialization format for a primitive class or struct.</summary>
/// <remarks>Defines the format used when serializing or deserializing representations such as JSON or XML.</remarks>
/// <param name="format">The serialization format string.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class SerializationFormatAttribute(string format) : Attribute
{
    /// <summary>Gets the serialization format string.</summary>
    public string Format { get; } = format;
}
