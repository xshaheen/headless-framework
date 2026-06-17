// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Exceptions;

public class JobValidatorException : Exception
{
    public JobValidatorException(string message)
        : base(message) => Errors = [message];

    public JobValidatorException(IReadOnlyList<string> errors)
        : base(string.Join("; ", errors)) => Errors = errors;

    /// <summary>
    /// The individual validation failures. A single-message exception exposes one entry; a batch enqueue that
    /// rejects multiple entities aggregates every failure here so the caller sees them all at once.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }
}
