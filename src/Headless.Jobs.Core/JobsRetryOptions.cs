// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Jobs.Enums;
using Headless.Jobs.Exceptions;
using Polly;
using Polly.Retry;

namespace Headless.Jobs;

/// <summary>Configures Polly retry execution and Jobs-owned exhaustion notification.</summary>
[PublicAPI]
public sealed class JobsRetryOptions
{
    /// <summary>
    /// Gets the default retry classification used by <see cref="RetryStrategy"/>: retry any
    /// exception that is not a cancellation and not a <see cref="TerminateExecutionException"/>.
    /// Reuse (or compose) this predicate when supplying a custom <see cref="RetryStrategy"/> value
    /// so replacing the strategy does not silently drop the framework's failure classification.
    /// </summary>
    public static Func<RetryPredicateArguments<object>, ValueTask<bool>> DefaultShouldHandle { get; } =
        static args =>
            ValueTask.FromResult(
                args.Outcome.Exception is { } exception
                    && exception is not OperationCanceledException
                    && exception is not TerminateExecutionException
            );

    /// <summary>
    /// Gets or sets the Polly retry strategy used for classification, delay generation, retry
    /// observation, and cancellation. Per-row <c>Retries</c> remains the durable retry budget.
    /// </summary>
    public RetryStrategyOptions RetryStrategy { get; set; } =
        new()
        {
            MaxRetryAttempts = int.MaxValue,
            Delay = TimeSpan.FromSeconds(30),
            BackoffType = DelayBackoffType.Constant,
            ShouldHandle = DefaultShouldHandle,
        };

    /// <summary>Gets or sets the callback invoked after an owned atomic transition to Failed.</summary>
    public Func<JobExhaustedContext, CancellationToken, Task>? OnExhausted { get; set; }

    /// <summary>Gets or sets the maximum callback duration. Defaults to 30 seconds.</summary>
    public TimeSpan OnExhaustedTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Jobs-owned context supplied to the exhausted callback.</summary>
/// <param name="JobId">Stable job identity.</param>
/// <param name="FunctionName">Registered function or handler identity.</param>
/// <param name="JobType">The durable job type.</param>
/// <param name="Exception">The final retryable exception.</param>
/// <param name="RetryCount">The durable retry count consumed.</param>
/// <param name="ServiceProvider">The fresh callback scope.</param>
[PublicAPI]
public sealed record JobExhaustedContext(
    Guid JobId,
    string FunctionName,
    JobType JobType,
    Exception Exception,
    int RetryCount,
    IServiceProvider ServiceProvider
);

internal sealed class JobsRetryOptionsValidator : AbstractValidator<JobsRetryOptions>
{
    public JobsRetryOptionsValidator()
    {
        RuleFor(x => x.RetryStrategy).NotNull();
        When(
            x => x.RetryStrategy is not null,
            () => RuleFor(x => x.RetryStrategy.MaxRetryAttempts).GreaterThanOrEqualTo(0)
        );
        RuleFor(x => x.OnExhaustedTimeout).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromHours(1));
    }
}
