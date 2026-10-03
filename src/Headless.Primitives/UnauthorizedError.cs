// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Primitives;

/// <summary>
/// Caller is not authenticated.
/// </summary>
[PublicAPI]
public sealed record UnauthorizedError : ApiResultError
{
    /// <summary>
    /// Cached singleton instance for common case.
    /// </summary>
    public static readonly UnauthorizedError Instance = new();

    /// <summary>Initializes the generic unauthorized error without a public descriptor.</summary>
    public UnauthorizedError() { }

    /// <summary>Initializes an unauthorized error carrying a structured descriptor.</summary>
    /// <param name="error">The descriptor exposed in the 401 ProblemDetails response.</param>
    public UnauthorizedError(ErrorDescriptor error)
    {
        Error = Argument.IsNotNull(error);
    }

    /// <summary>The optional descriptor exposed in the 401 ProblemDetails response.</summary>
    public ErrorDescriptor? Error { get; }

    /// <inheritdoc/>
    public override string Code => Error?.Code ?? "unauthorized";

    /// <inheritdoc/>
    public override string Message => Error?.Description ?? "Authentication required.";

    /// <summary>Determines equality from descriptor presence plus the public code and message.</summary>
    /// <param name="other">The unauthorized error to compare with.</param>
    /// <returns><see langword="true"/> when both errors carry the same code and message.</returns>
    public bool Equals(UnauthorizedError? other) =>
        other is not null
        && (Error is null) == (other.Error is null)
        && string.Equals(Code, other.Code, StringComparison.Ordinal)
        && string.Equals(Message, other.Message, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Error is not null, Code, Message);
}
