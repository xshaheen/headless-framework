// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;

namespace Headless.Jobs;

/// <summary>Configures the notification a host receives when a job run fails terminally.</summary>
/// <remarks>
/// Retry behavior is not configured here. A job's failure policy decides which failures end the run at once and how
/// long a retry waits when the row stores no interval, and the row's <c>Retries</c> budget decides how many retries
/// remain. No host-wide setting caps or overrides either.
/// </remarks>
[PublicAPI]
public sealed class JobsRetryOptions
{
    /// <summary>
    /// Gets or sets the callback invoked once after each owned atomic transition of a run to <c>Failed</c>: the retry
    /// budget ran out, a fail rule matched, cancellation the executor does not own ended the run, or crash recovery
    /// found the budget already consumed. It is not invoked when the handler ends its own run with a
    /// <see cref="TerminateExecutionException"/>, or when another node already wrote the terminal status.
    /// </summary>
    public Func<JobExhaustedContext, CancellationToken, Task>? OnExhausted { get; set; }

    /// <summary>Gets or sets the maximum callback duration. Defaults to 30 seconds.</summary>
    public TimeSpan OnExhaustedTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

internal sealed class JobsRetryOptionsValidator : AbstractValidator<JobsRetryOptions>
{
    public JobsRetryOptionsValidator()
    {
        RuleFor(x => x.OnExhaustedTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromHours(1));
    }
}
