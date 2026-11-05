// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// What a cron expression and zone would do: its next fire instants and a plain reading, or why it cannot be scheduled.
/// </summary>
/// <param name="NextFiresUtc">The next fire instants (UTC), earliest first; empty when <paramref name="ValidationMessage"/> is set.</param>
/// <param name="Reading">A plain-English reading of the expression, or <see langword="null"/> when it does not parse.</param>
/// <param name="ValidationMessage">Why the expression and zone cannot be scheduled, or <see langword="null"/> when they can.</param>
internal sealed record CronSchedulePreview(
    IReadOnlyList<DateTime> NextFiresUtc,
    string? Reading,
    string? ValidationMessage
)
{
    /// <summary>Gets a value indicating whether the expression and zone can be scheduled.</summary>
    public bool IsValid => ValidationMessage is null;

    public static CronSchedulePreview Invalid(string message, string? reading = null) => new([], reading, message);
}
