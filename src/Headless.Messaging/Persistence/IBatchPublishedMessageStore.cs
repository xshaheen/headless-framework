// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.Messaging.Persistence;

/// <summary>
/// A storage that writes several published rows of one transaction in one round trip. The rows are the ones
/// <see cref="CreatePublishedMessage" /> built, so each keeps the identifier its publish already returned.
/// </summary>
internal interface IBatchPublishedMessageStore
{
    /// <summary>
    /// Builds the row a publish of <paramref name="message" /> stores, with its storage identifier assigned, as
    /// <see cref="IDataStorage.StoreMessageAsync(string, MediumMessage, DbTransaction?, CancellationToken)" /> would.
    /// </summary>
    MediumMessage CreatePublishedMessage(MediumMessage message, DateTimeOffset? publishAt);

    /// <summary>
    /// Inserts <paramref name="rows" /> in order inside <paramref name="transaction" />, each as its own statement, and
    /// fills in the times the database stamped on each.
    /// </summary>
    ValueTask StorePublishedMessagesAsync(
        IReadOnlyList<(string Name, MediumMessage Message)> rows,
        DbTransaction transaction,
        CancellationToken cancellationToken
    );
}
