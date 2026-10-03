// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Encapsulates structured error information from a failed operation.
/// This record provides a standardized way to report operation errors with code and description.
/// </summary>
[PublicAPI]
public readonly record struct OperateError
{
    /// <summary>
    /// Gets the error code identifying the type or source of the error.
    /// This might be a string representation of a numeric error code, a category name, or other identifier.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Gets a human-readable description of the error.
    /// This typically explains what went wrong and may include suggestions for resolution.
    /// </summary>
    public required string Description { get; init; }
}
