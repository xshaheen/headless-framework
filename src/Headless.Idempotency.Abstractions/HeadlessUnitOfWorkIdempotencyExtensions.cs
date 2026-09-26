// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Idempotency;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// Adds the enlisted idempotency accessor to <see cref="IUnitOfWork" />, so an admission, fence, or completion commits
/// or rolls back with the business writes of the unit that makes it.
/// </summary>
[PublicAPI]
public static class HeadlessUnitOfWorkIdempotencyExtensions
{
    private const string _NotRegisteredMessage =
        "No idempotency provider is registered for this unit of work. Configure AddHeadlessIdempotency with "
        + "UsePostgreSql or UseSqlServer.";

    extension(IUnitOfWork unitOfWork)
    {
        /// <summary>
        /// Gets durable idempotency bound to this handle: every call through it runs inside this unit's transaction.
        /// </summary>
        /// <remarks>
        /// Free to read at each call site: the binding is created once per unit, on the first read, and kept as
        /// unit-local state. Reading it on a unit that already completed or rolled back throws.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The unit of work is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">
        /// No idempotency provider is registered in this host, or the unit is no longer active.
        /// </exception>
        /// <exception cref="ObjectDisposedException">This handle was disposed.</exception>
        public UnitOfWorkIdempotency Idempotency
        {
            get
            {
                Argument.IsNotNull(unitOfWork);

                var feature =
                    unitOfWork.GetFeature<IUnitOfWorkIdempotency>()
                    ?? throw new InvalidOperationException(_NotRegisteredMessage);

                return unitOfWork.GetOrAdd(
                    feature,
                    static (unit, idempotency) => new UnitOfWorkIdempotency(idempotency, unit)
                );
            }
        }
    }
}
