// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Primitives;

/// <summary>
/// Operation not permitted for current user/context.
/// </summary>
[PublicAPI]
public sealed record ForbiddenError : ApiResultError
{
    /// <summary>Initializes a forbidden error carrying a structured descriptor.</summary>
    /// <param name="error">The descriptor exposed in the 403 ProblemDetails response.</param>
    public ForbiddenError(ErrorDescriptor error)
    {
        Error = Argument.IsNotNull(error);
    }

    /// <summary>The descriptor exposed in the 403 ProblemDetails response.</summary>
    public ErrorDescriptor Error { get; }

    /// <summary>The reason the operation is not permitted.</summary>
    public string Reason => Error.Description;

    /// <inheritdoc/>
    public override string Code => Error.Code;

    /// <inheritdoc/>
    public override string Message => Error.Description;

    /// <summary>Determines equality from the public code and message, independent of descriptor identity.</summary>
    /// <param name="other">The forbidden error to compare with.</param>
    /// <returns><see langword="true"/> when both errors carry the same code and message.</returns>
    public bool Equals(ForbiddenError? other) =>
        other is not null
        && string.Equals(Code, other.Code, StringComparison.Ordinal)
        && string.Equals(Message, other.Message, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Code, Message);
}
