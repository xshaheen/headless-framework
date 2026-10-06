// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Jobs;

/// <summary>
/// How far a running job has got: a percentage and an optional message, as last reported through
/// <see cref="JobContext.ReportProgress"/>.
/// </summary>
/// <remarks>
/// Each report replaces the previous one whole, so a report without a message clears the stored message.
/// </remarks>
[PublicAPI]
public readonly record struct JobProgress
{
    /// <summary>The longest <see cref="Message"/> a report may carry, in characters.</summary>
    public const int MessageMaxLength = 512;

    /// <summary>Initializes a validated progress value.</summary>
    /// <param name="percent">Completion from <c>0</c> to <c>100</c>, inclusive.</param>
    /// <param name="message">Optional human-readable status, at most <see cref="MessageMaxLength"/> characters.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="percent"/> is outside <c>0</c>–<c>100</c> or is not a finite number, or
    /// <paramref name="message"/> is longer than <see cref="MessageMaxLength"/>.
    /// </exception>
    public JobProgress(double percent, string? message = null)
    {
        // IsInclusiveBetween also rejects NaN: double.NaN compares below every number.
        Percent = Argument.IsInclusiveBetween(percent, 0d, 100d);
        if (message is not null)
        {
            Argument.HasMaxLength(message, MessageMaxLength);
        }

        Message = message;
    }

    /// <summary>Completion from <c>0</c> to <c>100</c>, inclusive.</summary>
    public double Percent { get; }

    /// <summary>Optional human-readable status; <see langword="null"/> when the report carried none.</summary>
    public string? Message { get; }
}
