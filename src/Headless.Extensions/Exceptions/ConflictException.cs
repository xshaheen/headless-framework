// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Exceptions;

/// <summary>
/// An exception that signals a business conflict (for example a uniqueness or invariant violation), carrying one
/// or more <see cref="ErrorDescriptor"/> entries that describe the conflicting conditions.
/// </summary>
[PublicAPI]
public sealed class ConflictException : Exception
{
    /// <summary>The error code applied to conflict errors that are created without an explicit code.</summary>
    public const string DefaultErrorCode = "error";

    /// <summary>Initializes a conflict exception from a single <see cref="ErrorDescriptor"/>.</summary>
    /// <param name="error">The descriptor of the conflicting condition.</param>
    public ConflictException(ErrorDescriptor error)
        : base(_BuildErrorMessage(error))
    {
        Errors = [error];
    }

    /// <summary>Initializes a conflict exception from a message and an optional error code.</summary>
    /// <param name="error">The human-readable conflict message.</param>
    /// <param name="code">The error code associated with the conflict; defaults to <see cref="DefaultErrorCode"/>.</param>
    public ConflictException([LocalizationRequired] string error, string code = DefaultErrorCode)
        : base(_BuildErrorMessage(error))
    {
        Errors = [new(code, error)];
    }

    /// <summary>Initializes a conflict exception from a message and the underlying exception that caused it.</summary>
    /// <param name="error">The human-readable conflict message.</param>
    /// <param name="inner">The exception that triggered this conflict.</param>
    public ConflictException([LocalizationRequired] string error, Exception inner)
        : base(_BuildErrorMessage(error), inner)
    {
        Errors = [new(DefaultErrorCode, error)];
    }

    /// <summary>Initializes a conflict exception from a list of <see cref="ErrorDescriptor"/> entries.</summary>
    /// <param name="errors">The descriptors of all conflicting conditions.</param>
    public ConflictException(IReadOnlyList<ErrorDescriptor> errors)
        : base(_BuildErrorMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>The error descriptors describing the conflicting conditions that caused this exception.</summary>
    public IReadOnlyList<ErrorDescriptor> Errors { get; }

    private static string _BuildErrorMessage(IEnumerable<ErrorDescriptor> errors)
    {
        var builder = new StringBuilder();

        builder.Append("Conflict:");

        foreach (var error in errors)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}-- {_BuildErrorMessage(error)}");
        }

        return builder.ToString();
    }

    private static string _BuildErrorMessage(ErrorDescriptor error) => $"Conflict: {error}";

    private static string _BuildErrorMessage(string error) => $"Conflict: {error}";
}
