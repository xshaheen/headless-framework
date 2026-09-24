// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.DistributedLocks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// Adds the enlisted advisory-lock accessor to <see cref="IUnitOfWork" />, so a lock that must die with the unit's
/// transaction is reached from the unit it joins and needs no provider transaction type to discover.
/// </summary>
[PublicAPI]
public static class HeadlessUnitOfWorkAdvisoryLocksExtensions
{
    private const string _NotRegisteredMessage =
        "No transaction-scoped advisory lock provider is registered for this unit of work. Configure "
        + "AddHeadlessDistributedLocks with UsePostgreSql or UseSqlServer; the Redis and InMemory providers hold "
        + "no transaction to scope a lock to.";

    extension(IUnitOfWork unitOfWork)
    {
        /// <summary>
        /// Gets the advisory locks bound to this handle: a lock taken through them lives inside this unit's
        /// transaction, and the unit's commit or rollback releases it.
        /// </summary>
        /// <remarks>
        /// A property, not an async factory, and free to read at each call site: the binding is created once per
        /// unit, on the first read, and kept as unit-local state, so later reads allocate nothing. Reading it on a
        /// unit that already completed or rolled back throws, and a binding kept from before that point throws on
        /// its next call. The provider check happens before the binding, so a host without a transaction-capable
        /// lock provider throws here rather than on the first lock.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The unit of work is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">
        /// No transaction-scoped advisory lock provider is registered in this host, or the unit is no longer
        /// active.
        /// </exception>
        /// <exception cref="ObjectDisposedException">This handle was disposed.</exception>
        public UnitOfWorkAdvisoryLocks AdvisoryLocks
        {
            get
            {
                Argument.IsNotNull(unitOfWork);

                var feature =
                    unitOfWork.GetFeature<IUnitOfWorkAdvisoryLocks>()
                    ?? throw new InvalidOperationException(_NotRegisteredMessage);

                return unitOfWork.GetOrAdd(feature, static (unit, locks) => new UnitOfWorkAdvisoryLocks(locks, unit));
            }
        }
    }
}
