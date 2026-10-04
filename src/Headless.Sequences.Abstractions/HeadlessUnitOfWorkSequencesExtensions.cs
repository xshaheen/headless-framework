// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sequences;

namespace Headless.UnitOfWork;

/// <summary>
/// Exposes gap-free sequence accessors on <see cref="IUnitOfWork" /> to coordinate counter allocations with business transactions.
/// </summary>
[PublicAPI]
public static class HeadlessUnitOfWorkSequencesExtensions
{
    private const string _NotRegisteredMessage =
        "No sequence provider is registered for this unit of work. Configure AddHeadlessSequences with "
        + "UsePostgreSql or UseSqlServer.";

    extension(IUnitOfWork unitOfWork)
    {
        /// <summary>
        /// Gets the gap-free sequences bound to this handle: a number taken through them is written inside
        /// this unit's transaction, and the unit's rollback returns it.
        /// </summary>
        /// <remarks>
        /// Free to read at each call site: the binding is created once per unit, on the first read, and kept
        /// as unit-local state. Reading it on a unit that already completed or rolled back throws.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="unitOfWork"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// No sequence provider is registered, or <paramref name="unitOfWork"/> is no longer active.
        /// </exception>
        /// <exception cref="ObjectDisposedException"><paramref name="unitOfWork"/> is disposed.</exception>
        public UnitOfWorkSequences Sequences
        {
            get
            {
                Argument.IsNotNull(unitOfWork);

                var feature =
                    unitOfWork.GetFeature<IUnitOfWorkSequences>()
                    ?? throw new InvalidOperationException(_NotRegisteredMessage);

                return unitOfWork.GetOrAdd(
                    feature,
                    static (unit, sequences) => new UnitOfWorkSequences(sequences, unit)
                );
            }
        }
    }
}
