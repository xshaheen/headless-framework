// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Hosting;

/// <summary>How one step of a resource or seed run ended.</summary>
internal enum BudgetedStepStatus
{
    Ok = 0,
    Failed = 1,
    Unsupported = 2,
    TimedOut = 3,
}

/// <summary>One step's result: its status, how long it ran, and its error when it did not succeed.</summary>
internal readonly record struct BudgetedStepResult(BudgetedStepStatus Status, TimeSpan Duration, Exception? Error);

/// <summary>Runs one step of a resource or seed run under the run's whole-run timeout budget.</summary>
internal static class BudgetedStep
{
    /// <summary>Creates the run's budget: the caller's token, canceled after <paramref name="timeout"/> too.</summary>
    public static CancellationTokenSource CreateBudget(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (timeout is { } limit)
        {
            // Real time on purpose: the budget bounds I/O against external systems, not an app schedule.
            budget.CancelAfter(limit);
        }

        return budget;
    }

    /// <summary>
    /// Runs <paramref name="step"/> with the budget's token. A <see cref="StatefulResourceUnsupportedException"/> is
    /// unsupported, any exception thrown once the budget has elapsed is a timeout, and any other exception is a failure;
    /// only the caller's own cancellation propagates.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<BudgetedStepResult> RunAsync(
        Func<CancellationToken, Task> step,
        CancellationToken budgetToken,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            await step(budgetToken).ConfigureAwait(false);

            return new(BudgetedStepStatus.Ok, Stopwatch.GetElapsedTime(started), Error: null);
        }
        catch (StatefulResourceUnsupportedException exception)
        {
            return new(BudgetedStepStatus.Unsupported, Stopwatch.GetElapsedTime(started), exception);
        }
#pragma warning disable CA1031, EPC12 // False positive: one step's failure is recorded in the report so the run can decide what to do next; the caller's own cancellation still propagates.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Drivers report a canceled command in their own exception type (SqlClient throws SqlException), not only as
            // OperationCanceledException, so the elapsed budget, not the exception type, says the step timed out.
            return budgetToken.IsCancellationRequested
                ? new(
                    BudgetedStepStatus.TimedOut,
                    Stopwatch.GetElapsedTime(started),
                    new TimeoutException("The timeout elapsed while this step was running.", exception)
                )
                : new(BudgetedStepStatus.Failed, Stopwatch.GetElapsedTime(started), exception);
        }
#pragma warning restore CA1031, EPC12
    }
}
