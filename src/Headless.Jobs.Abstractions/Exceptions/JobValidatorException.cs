// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Exceptions;

/// <summary>
/// Thrown when one or more jobs fail pre-persistence validation: unknown function names, unparseable
/// cron expressions, or other constraint violations detected before the job row is written.
/// </summary>
/// <remarks>
/// Batch operations aggregate all per-entity failures into <see cref="Errors"/> so the caller sees
/// every problem at once. Single-entity operations produce exactly one entry.
/// </remarks>
public class JobValidatorException : Exception
{
    /// <summary>
    /// Initializes a new instance with a single validation failure message.
    /// </summary>
    /// <param name="message">The validation failure description.</param>
    public JobValidatorException(string message)
        : base(message) => Errors = [message];

    /// <summary>
    /// Initializes a new instance aggregating multiple validation failures into one exception.
    /// </summary>
    /// <param name="errors">All individual failure messages; joined with "; " as the exception message.</param>
    public JobValidatorException(IReadOnlyList<string> errors)
        : base(string.Join("; ", errors)) => Errors = errors;

    /// <summary>
    /// The individual validation failures. A single-message exception exposes one entry; a batch enqueue that
    /// rejects multiple entities aggregates every failure here so the caller sees them all at once.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }
}
