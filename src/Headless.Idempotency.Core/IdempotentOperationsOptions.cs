// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Idempotency;

/// <summary>Defaults for admissions and the schedule of the retention purge.</summary>
[PublicAPI]
public sealed class IdempotentOperationsOptions
{
    /// <summary>
    /// Gets or sets how long a record replays after completion when the caller does not pass a retention. Default:
    /// 24 hours.
    /// </summary>
    public TimeSpan DefaultRetention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets how long an admitted attempt owns its key, unless renewed, when the caller does not pass a lease
    /// duration. A crashed attempt blocks its key this long before the next admission can take it over. Must fall
    /// within <see cref="MinimumLeaseDuration" /> and <see cref="MaximumLeaseDuration" />. Default: 2 minutes.
    /// </summary>
    public TimeSpan DefaultLeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets the shortest lease duration an admission or renewal accepts. Default: 1 second.
    /// </summary>
    public TimeSpan MinimumLeaseDuration { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the longest lease duration an admission or renewal accepts. It bounds how long a crashed attempt
    /// blocks its key. Default: 1 day.
    /// </summary>
    public TimeSpan MaximumLeaseDuration { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Gets or sets how often the retention purge deletes records past their retention whose lease is no longer live.
    /// <see langword="null" /> disables the purge, for hosts that run it elsewhere or keep
    /// records forever. Default: 1 hour.
    /// </summary>
    public TimeSpan? PurgeInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the most records one purge statement deletes, which bounds how long it holds locks. A purge run
    /// repeats batches until one comes back short. Default: 1000.
    /// </summary>
    public int PurgeBatchSize { get; set; } = 1000;
}

internal sealed class IdempotentOperationsOptionsValidator : AbstractValidator<IdempotentOperationsOptions>
{
    public IdempotentOperationsOptionsValidator()
    {
        RuleFor(x => x.DefaultRetention).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.MinimumLeaseDuration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.MaximumLeaseDuration).GreaterThanOrEqualTo(x => x.MinimumLeaseDuration);
        RuleFor(x => x.DefaultLeaseDuration)
            .GreaterThanOrEqualTo(x => x.MinimumLeaseDuration)
            .LessThanOrEqualTo(x => x.MaximumLeaseDuration);
        RuleFor(x => x.PurgeInterval).GreaterThan(TimeSpan.Zero).When(x => x.PurgeInterval is not null);
        RuleFor(x => x.PurgeBatchSize).GreaterThan(0);
    }
}
