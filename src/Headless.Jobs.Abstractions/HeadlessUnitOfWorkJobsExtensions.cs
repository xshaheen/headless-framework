// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// Adds the enlisted Jobs receivers to <see cref="IUnitOfWork" />, so a job scheduled inside a transaction is
/// reached from the unit it enlists in and needs no second import to discover.
/// </summary>
[PublicAPI]
public static class HeadlessUnitOfWorkJobsExtensions
{
    private const string _NotRegisteredMessage =
        "No Jobs feature is registered for this unit of work. Call AddHeadlessJobs during startup to register it.";

    extension(IUnitOfWork unitOfWork)
    {
        /// <summary>
        /// Gets a scheduler bound to this unit: every schedule writes its job row inside this unit's transaction,
        /// and a rollback discards it. The injected <see cref="IJobScheduler" /> is the autonomous counterpart.
        /// </summary>
        /// <remarks>
        /// A property, cheap enough to read at each call site: it resolves the singleton Jobs feature and binds it
        /// to this handle. Liveness and the unit's relational resource are checked when a schedule runs.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The unit of work is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">No Jobs feature is registered in this host.</exception>
        /// <exception cref="ObjectDisposedException">This handle was disposed.</exception>
        public IJobScheduler Jobs => _Feature(unitOfWork).Bind(unitOfWork);

        /// <summary>Gets the low-level time-job manager bound to this unit; see <see cref="Jobs" />.</summary>
        /// <typeparam name="TTimeJob">The host's time-job entity type.</typeparam>
        public ITimeJobManager<TTimeJob> TimeJobs<TTimeJob>()
            where TTimeJob : TimeJobEntity<TTimeJob>, new() => _Feature(unitOfWork).BindTimeJobs<TTimeJob>(unitOfWork);

        /// <summary>Gets the low-level cron-job manager bound to this unit; see <see cref="Jobs" />.</summary>
        /// <typeparam name="TCronJob">The host's cron-job entity type.</typeparam>
        public ICronJobManager<TCronJob> CronJobs<TCronJob>()
            where TCronJob : CronJobEntity, new() => _Feature(unitOfWork).BindCronJobs<TCronJob>(unitOfWork);
    }

    private static IUnitOfWorkJobs _Feature(IUnitOfWork unitOfWork)
    {
        Argument.IsNotNull(unitOfWork);

        return unitOfWork.GetFeature<IUnitOfWorkJobs>() ?? throw new InvalidOperationException(_NotRegisteredMessage);
    }
}
