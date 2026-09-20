// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// Adds the messaging outbox accessor to <see cref="IUnitOfWork" />, so an enlisted publish is reached from the
/// unit it enlists in and needs no second import to discover.
/// </summary>
[PublicAPI]
public static class HeadlessUnitOfWorkOutboxExtensions
{
    extension(IUnitOfWork unitOfWork)
    {
        /// <summary>
        /// Gets the messaging outbox bound to this handle: publishing through it writes the durable row inside
        /// this unit's transaction, and a rollback discards the message.
        /// </summary>
        /// <remarks>
        /// A property, not an async factory, and free to read at each call site: the binding is created once per
        /// unit, on the first read, and kept as unit-local state, so later reads allocate nothing. Reading it on a
        /// unit that already completed or rolled back throws, and a binding kept from before that point throws on
        /// its next publish.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The unit of work is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">
        /// No messaging outbox is registered in this host, or the unit is no longer active.
        /// </exception>
        /// <exception cref="ObjectDisposedException">This handle was disposed.</exception>
        public UnitOfWorkOutbox Outbox
        {
            get
            {
                Argument.IsNotNull(unitOfWork);

                return unitOfWork.GetOrAdd(
                    unitOfWork,
                    static (unit, _) =>
                    {
                        var outbox =
                            unit.GetFeature<IUnitOfWorkOutbox>()
                            ?? throw new InvalidOperationException(
                                "No messaging outbox is registered for this unit of work. Call AddHeadlessMessaging during startup to register it."
                            );

                        return new UnitOfWorkOutbox(outbox, unit);
                    }
                );
            }
        }
    }
}
