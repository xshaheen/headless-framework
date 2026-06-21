// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.DistributedLocks;
using Headless.Jobs.Entities;
using Headless.Jobs.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>
/// <c>UseDistributedLock</c> builder extensions for Jobs. These live in <c>Headless.Jobs.Core</c> (not
/// <c>Headless.Jobs.Abstractions</c>) so the abstractions package carries no dependency on a distributed-lock
/// package — mirroring how <c>Headless.Messaging</c> keeps <c>UseDistributedLock</c> on its Core builder.
/// </summary>
public static class JobsDistributedLockExtensions
{
    /// <summary>
    /// Registers an <see cref="IDistributedLock"/> for the Jobs-scoped lock slot and enables
    /// <see cref="SchedulerOptionsBuilder.UseStorageLock"/>. The lock coarse-gates startup cron-seed migration so N
    /// booting nodes neither redundantly re-run the same scan/upsert nor (on simultaneous first-boot) insert duplicate
    /// seed rows. Dead-node resource reclaim is intentionally left unguarded — see <c>JobsDeadOwnerReclaimer</c>.
    /// </summary>
    /// <param name="builder">The Jobs options builder.</param>
    /// <param name="provider">The lock provider instance to use for Jobs coordination.</param>
    /// <remarks>
    /// Best-effort duplicate-suppression, not the job-execution correctness boundary: per-row predicates,
    /// <c>node@incarnation</c> ownership, and per-job leases stay that boundary. Jobs keeps the provider under a
    /// Jobs-private keyed-DI key so it never conflicts with any application-level <see cref="IDistributedLock"/>.
    /// Last-wins: calling this (or its factory overload) more than once replaces any prior Jobs lock registration.
    /// </remarks>
    public static JobsOptionsBuilder<TTimeJob, TCronJob> UseDistributedLock<TTimeJob, TCronJob>(
        this JobsOptionsBuilder<TTimeJob, TCronJob> builder,
        IDistributedLock provider
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(provider);

        builder.LockRegistrationAction = services => services.AddKeyedSingleton(JobsKeys.LockProvider, provider);
        builder.SchedulerOptions.UseStorageLock = true;
        return builder;
    }

    /// <summary>
    /// Registers a factory-resolved <see cref="IDistributedLock"/> for the Jobs-scoped lock slot and enables
    /// <see cref="SchedulerOptionsBuilder.UseStorageLock"/>. Use this overload when the provider itself depends on
    /// other DI-registered services.
    /// </summary>
    /// <param name="builder">The Jobs options builder.</param>
    /// <param name="factory">A factory that receives the <see cref="IServiceProvider"/> and returns the lock provider.</param>
    /// <remarks>See the instance overload for the correctness/optimization contract.</remarks>
    public static JobsOptionsBuilder<TTimeJob, TCronJob> UseDistributedLock<TTimeJob, TCronJob>(
        this JobsOptionsBuilder<TTimeJob, TCronJob> builder,
        Func<IServiceProvider, IDistributedLock> factory
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(factory);

        builder.LockRegistrationAction = services =>
            services.AddKeyedSingleton<IDistributedLock>(JobsKeys.LockProvider, (sp, _) => factory(sp));
        builder.SchedulerOptions.UseStorageLock = true;
        return builder;
    }
}
