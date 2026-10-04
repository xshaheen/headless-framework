// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;

namespace Headless.Generator.Primitives;

/// <summary>
/// Thrown when a primitive value violates domain constraints or validation rules.
/// </summary>
/// <param name="message">The reason for the validation failure.</param>
/// <param name="instance">The invalid primitive instance.</param>
[method: EditorBrowsable(EditorBrowsableState.Never)]
public sealed class InvalidPrimitiveValueException(string message, IPrimitive instance)
    : Exception(_GenerateErrorMessage(message, instance))
{
    private static string _GenerateErrorMessage(string message, IPrimitive value)
    {
        var type = value.GetType();
        var typeName = type.FullName ?? type.Name;

        return $"Cannot create instance of '{typeName}'. {message}";
    }
}
