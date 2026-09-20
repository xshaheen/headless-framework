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
        /// A property, not an async factory, and cheap enough to read at each call site: it resolves the singleton
        /// outbox feature from the unit's scope and binds it to the handle it was read from. Liveness is checked
        /// when a publish runs rather than here.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The unit of work is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">No messaging outbox is registered in this host.</exception>
        /// <exception cref="ObjectDisposedException">This handle was disposed.</exception>
        public UnitOfWorkOutbox Outbox
        {
            get
            {
                Argument.IsNotNull(unitOfWork);

                var outbox =
                    unitOfWork.GetFeature<IUnitOfWorkOutbox>()
                    ?? throw new InvalidOperationException(
                        "No messaging outbox is registered for this unit of work. Call AddHeadlessMessaging during startup to register it."
                    );

                return new UnitOfWorkOutbox(outbox, unitOfWork);
            }
        }
    }
}
