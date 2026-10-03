// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>The severity assigned to an <see cref="ErrorDescriptor"/>.</summary>
[PublicAPI]
public enum ValidationSeverity
{
    /// <summary>Informational message; does not indicate a failure.</summary>
    Information = 0,

    /// <summary>A warning that does not by itself block the operation.</summary>
    Warning = 1,

    /// <summary>An error indicating the operation failed validation.</summary>
    Error = 2,
}
