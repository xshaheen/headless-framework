// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;

namespace Headless.Messaging.Internal;

/// <summary>
/// Coordinated write seam for storages that join a commit scope without a relational transaction.
/// </summary>
/// <remarks>
/// <see cref="IDataStorage.StoreMessageAsync(string, MediumMessage, System.Data.Common.DbTransaction?, CancellationToken)" />
/// can only carry a database transaction, so a storage whose atomicity comes from the coordinator itself
/// (in-memory today) receives the coordinator through this seam instead. Rows written here must stay invisible to
/// pickup and monitoring until the coordinator commits and must be discarded when it rolls back. The storage must
/// enlist its commit work on the coordinator inside this call, before <see cref="OutboxMessageWriter" /> enlists
/// the dispatcher hand-off, so the committed row is visible before the dispatcher receives it.
/// </remarks>
internal interface ICoordinatedMessageStore
{
    ValueTask<MediumMessage> StoreCoordinatedMessageAsync(
        string name,
        MediumMessage message,
        DateTimeOffset? publishAt,
        ICommitCoordinator coordinator,
        CancellationToken cancellationToken
    );
}
