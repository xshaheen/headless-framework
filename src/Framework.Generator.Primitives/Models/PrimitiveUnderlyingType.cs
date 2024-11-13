// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Generator.Primitives.Models;

/// <summary>Enumerates the possible types of Primitive values.</summary>
internal enum PrimitiveUnderlyingType
{
    // Represents a string Primitive type.
    String,

    // Represents a Guid Primitive type.
    Guid,

    // Represents a boolean Primitive type.
    Boolean,

    // Represents a signed byte Primitive type.
    SByte,

    // Represents an unsigned byte Primitive type.
    Byte,

    // Represents a signed 16-bit integer Primitive type.
    Int16,

    // Represents an unsigned 16-bit integer Primitive type.
    UInt16,

    // Represents a signed 32-bit integer Primitive type.
    Int32,

    // Represents an unsigned 32-bit integer Primitive type.
    UInt32,

    // Represents a signed 64-bit integer Primitive type.
    Int64,

    // Represents an unsigned 64-bit integer Primitive type.
    UInt64,

    // Represents a decimal Primitive type.
    Decimal,

    // Represents a single-precision floating-point Primitive type.
    Single,

    // Represents a double-precision floating-point Primitive type.
    Double,

    // Represents a DateTime Primitive type.
    DateTime,

    // Represents a DateOnly Primitive type.
    DateOnly,

    // Represents a TimeOnly Primitive type.
    TimeOnly,

    // Represents a TimeSpan Primitive type.
    TimeSpan,

    // Represents a DateTimeOffset Primitive type.
    DateTimeOffset,

    // Represents a character Primitive type.
    Char,

    // Represents another Primitive type.
    Other,
}
