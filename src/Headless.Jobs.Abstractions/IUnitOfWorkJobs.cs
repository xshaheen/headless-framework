// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.UnitOfWork;

namespace Headless.Jobs;

/// <summary>
/// Plumbing behind <c>unit.Jobs</c>: the enlisted scheduling feature <c>AddHeadlessJobs</c> registers, which a
/// unit of work resolves through <see cref="IUnitOfWork.GetFeature{TFeature}" />. Application code uses the
/// accessors on the unit, which supply it; this interface is public only so the unit-of-work packages can hand it
/// out without referencing Jobs.
/// </summary>
/// <remarks>
/// Every receiver returned here writes inside the given unit's transaction — the row becomes visible when the
/// unit completes and is discarded when it rolls back — and refuses rather than degrades when the unit carries no
/// live relational resource for the job store. An injected <see cref="IJobScheduler" /> or manager is the
/// autonomous counterpart, whose rows survive the caller's rollback.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUnitOfWorkJobs : IUnitOfWorkFeature
{
    /// <summary>Returns a scheduler whose every write enlists in <paramref name="unitOfWork" />.</summary>
    /// <param name="unitOfWork">The unit whose transaction the job rows are written in.</param>
    IJobScheduler Bind(IUnitOfWork unitOfWork);

    /// <summary>Returns a time-job manager whose every write enlists in <paramref name="unitOfWork" />.</summary>
    /// <typeparam name="TTimeJob">The host's time-job entity type, as registered with <c>AddHeadlessJobs</c>.</typeparam>
    /// <param name="unitOfWork">The unit whose transaction the job rows are written in.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="TTimeJob" /> is not the registered entity type.</exception>
    ITimeJobManager<TTimeJob> BindTimeJobs<TTimeJob>(IUnitOfWork unitOfWork)
        where TTimeJob : TimeJobEntity<TTimeJob>, new();

    /// <summary>Returns a cron-job manager whose every write enlists in <paramref name="unitOfWork" />.</summary>
    /// <typeparam name="TCronJob">The host's cron-job entity type, as registered with <c>AddHeadlessJobs</c>.</typeparam>
    /// <param name="unitOfWork">The unit whose transaction the job rows are written in.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="TCronJob" /> is not the registered entity type.</exception>
    ICronJobManager<TCronJob> BindCronJobs<TCronJob>(IUnitOfWork unitOfWork)
        where TCronJob : CronJobEntity, new();
}
