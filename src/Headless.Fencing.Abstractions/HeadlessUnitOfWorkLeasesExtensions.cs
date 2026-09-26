// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Fencing;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// Adds the enlisted lease accessor to <see cref="IUnitOfWork" />, so a grant, fence, or settlement commits or rolls
/// back with the business writes of the unit that makes it.
/// </summary>
[PublicAPI]
public static class HeadlessUnitOfWorkLeasesExtensions
{
    private const string _NotRegisteredMessage =
        "No fencing provider is registered for this unit of work. Configure AddHeadlessFencing with "
        + "UsePostgreSql or UseSqlServer.";

    extension(IUnitOfWork unitOfWork)
    {
        /// <summary>
        /// Gets the fenced leases bound to this handle: every call through them runs inside this unit's transaction.
        /// </summary>
        /// <remarks>
        /// Free to read at each call site: the binding is created once per unit, on the first read, and kept as
        /// unit-local state. Reading it on a unit that already completed or rolled back throws.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The unit of work is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">
        /// No fencing provider is registered in this host, or the unit is no longer active.
        /// </exception>
        /// <exception cref="ObjectDisposedException">This handle was disposed.</exception>
        public UnitOfWorkLeases Leases
        {
            get
            {
                Argument.IsNotNull(unitOfWork);

                var feature =
                    unitOfWork.GetFeature<IUnitOfWorkLeases>()
                    ?? throw new InvalidOperationException(_NotRegisteredMessage);

                return unitOfWork.GetOrAdd(feature, static (unit, leases) => new UnitOfWorkLeases(leases, unit));
            }
        }
    }
}
