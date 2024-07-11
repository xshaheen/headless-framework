// ReSharper disable once CheckNamespace

namespace Primitives;

/// <summary>
/// Specifies the serialization format for a class or struct.
/// </summary>
/// <remarks>
/// This attribute can be applied to classes or structs to indicate the format used for serialization
/// when converting the object to or from a serialized representation, such as JSON or XML.
/// </remarks>
/// <param name="format">The serialization format as a string.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class SerializationFormatAttribute(string format) : Attribute
{
    /// <summary>Gets or sets the serialization format as a string.</summary>
    public string Format { get; } = format;
}
