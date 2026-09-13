// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;

namespace Headless.Messaging.Storage.InMemory;

internal sealed partial class InMemoryDataStorage : IMessageRevocationStorage
{
    public ValueTask<MessageRevocationResult> RevokeAsync(Guid storageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!PublishedMessages.TryGetValue(storageId, out var message))
        {
            return ValueTask.FromResult(MessageRevocationResult.NotFound);
        }

        lock (message)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !PublishedMessages.TryGetValue(storageId, out var current)
                || !ReferenceEquals(current, message)
                || !string.Equals(message.Version, messagingOptions.Value.Version, StringComparison.Ordinal)
            )
            {
                return ValueTask.FromResult(MessageRevocationResult.NotFound);
            }

            if (
                message.StatusName is StatusName.Succeeded or StatusName.Failed
                || message.InlineAttempts != 0
                || message.Retries != 0
                || message.NextRetryAt is not null
            )
            {
                return ValueTask.FromResult(MessageRevocationResult.AttemptReserved);
            }

            return ValueTask.FromResult(
                PublishedMessages.TryRemove(new KeyValuePair<Guid, MemoryMessage>(storageId, message))
                    ? MessageRevocationResult.Revoked
                    : MessageRevocationResult.NotFound
            );
        }
    }
}
