// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

/// <summary>
/// Coordinated write seam for storages that join a unit of work without a relational transaction.
/// </summary>
/// <remarks>
/// <see cref="IDataStorage.StoreMessageAsync(string, MediumMessage, System.Data.Common.DbTransaction?, CancellationToken)" />
/// can only carry a database transaction, so a storage whose atomicity comes from the unit of work itself
/// (in-memory today) receives the unit through this seam instead. Rows written here must stay invisible to
/// pickup and monitoring until the unit completes and must be discarded when it fails. The storage must
/// register its completion work on the unit inside this call, before <see cref="OutboxMessageWriter" /> enlists
/// the dispatcher hand-off, so the committed row is visible before the dispatcher receives it.
/// </remarks>
internal interface ICoordinatedMessageStore
{
    ValueTask<MediumMessage> StoreCoordinatedMessageAsync(
        string name,
        MediumMessage message,
        DateTimeOffset? publishAt,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken
    );
}
