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
    /// duration. A crashed attempt blocks its key this long before the next admission can take it over. Must also fall
    /// within the fencing duration bounds. Default: 2 minutes.
    /// </summary>
    public TimeSpan DefaultLeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets how often the retention purge deletes records past their retention, together with the fenced
    /// leases they left behind. <see langword="null" /> disables the purge, for hosts that run it elsewhere or keep
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
        RuleFor(x => x.DefaultLeaseDuration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.PurgeInterval).GreaterThan(TimeSpan.Zero).When(x => x.PurgeInterval is not null);
        RuleFor(x => x.PurgeBatchSize).GreaterThan(0);
    }
}
