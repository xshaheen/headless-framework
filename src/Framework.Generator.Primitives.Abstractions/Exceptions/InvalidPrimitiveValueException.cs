// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.ComponentModel;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Primitives;

/// <summary>
/// Represents an exception thrown when a value does not conform to the constraints or rules
/// defined within a specific domain context.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="InvalidPrimitiveValueException"/> class with a specific error message.
/// </remarks>
/// <param name="message">The error message that describes the reason for the exception.</param>
/// <param name="instance">actual instance of primitive</param>
[method: EditorBrowsable(EditorBrowsableState.Never)]
public class InvalidPrimitiveValueException(string message, IPrimitive instance)
    : Exception(_GenerateErrorMessage(message, instance))
{
    /// <summary>Generates the error message for the <see cref="InvalidPrimitiveValueException"/>.</summary>
    /// <param name="message">The error message that describes the reason for the exception.</param>
    /// <param name="value">The actual value of the primitive.</param>
    /// <returns>The generated error message.</returns>
    private static string _GenerateErrorMessage(string message, IPrimitive value)
    {
        var type = value.GetType();
        var typeName = type.FullName ?? type.Name;

        return $"Cannot create instance of '{typeName}'. {message}";
    }
}
