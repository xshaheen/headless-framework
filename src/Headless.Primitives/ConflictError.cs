// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Primitives;

/// <summary>
/// Business rule conflict (duplicate, invalid state, etc.).
/// </summary>
[PublicAPI]
public sealed record ConflictError : ApiResultError
{
    /// <summary>Initializes a conflict from a code and message.</summary>
    /// <param name="code">A machine-readable code describing the conflict.</param>
    /// <param name="message">A human-readable message describing the conflict.</param>
    public ConflictError(string code, string message)
        : this(new ErrorDescriptor(code, message)) { }

    /// <summary>Initializes a conflict from one descriptor.</summary>
    /// <param name="error">The descriptor describing the conflict.</param>
    public ConflictError(ErrorDescriptor error)
    {
        Errors = [Argument.IsNotNull(error)];
    }

    /// <summary>Initializes a conflict from one or more descriptors.</summary>
    /// <param name="errors">The descriptors describing all conflicting conditions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="errors"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="errors"/> is empty or contains a <see langword="null"/> item.</exception>
    public ConflictError(params IReadOnlyCollection<ErrorDescriptor> errors)
    {
        var checkedErrors = Argument.IsNotNullOrEmpty(errors);
        Argument.HasNoNulls(checkedErrors);
        Errors = [.. checkedErrors];
    }

    /// <summary>The descriptors describing all conflicting conditions.</summary>
    public IReadOnlyList<ErrorDescriptor> Errors { get; }

    /// <inheritdoc/>
    public override string Code => Errors.Count == 1 ? Errors[0].Code : "conflict:multiple_errors";

    /// <inheritdoc/>
    public override string Message => Errors.Count == 1 ? Errors[0].Description : $"{Errors.Count} conflicts occurred.";

    /// <summary>Determines equality from the ordered descriptor codes and messages.</summary>
    /// <param name="other">The conflict error to compare with.</param>
    /// <returns><see langword="true"/> when both errors carry equivalent descriptors in the same order.</returns>
    public bool Equals(ConflictError? other) =>
        other is not null && ApiResultErrorEquality.SequenceEquals(Errors, other.Errors);

    /// <inheritdoc/>
    public override int GetHashCode() => ApiResultErrorEquality.GetSequenceHashCode(Errors);
}
