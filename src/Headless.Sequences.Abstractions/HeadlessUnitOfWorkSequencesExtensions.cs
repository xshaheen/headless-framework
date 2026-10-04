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
        /// Gets the gap-free sequence coordinator bound to this unit of work.
        /// </summary>
        /// <remarks>
        /// Allocates a binding on first access and caches it within unit state.
        /// Accessing this property after completing or rolling back the unit throws.
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
