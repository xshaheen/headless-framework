// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sequences;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// Adds the gap-free sequence accessor to <see cref="IUnitOfWork" />, so a number that must roll back with the
/// business write is taken from the unit that writes it.
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
        /// Gets the gap-free sequences bound to this handle: a number taken through them is written inside this
        /// unit's transaction, and the unit's rollback returns it.
        /// </summary>
        /// <remarks>
        /// Free to read at each call site: the binding is created once per unit, on the first read, and kept as
        /// unit-local state. Reading it on a unit that already completed or rolled back throws.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The unit of work is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">
        /// No sequence provider is registered in this host, or the unit is no longer active.
        /// </exception>
        /// <exception cref="ObjectDisposedException">This handle was disposed.</exception>
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
