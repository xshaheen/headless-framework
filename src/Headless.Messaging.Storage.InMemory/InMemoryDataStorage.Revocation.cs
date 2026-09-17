// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;

namespace Headless.Messaging.Storage.InMemory;

internal sealed partial class InMemoryDataStorage : IMessageRevocationStorage
{
    private bool _IsRevocationEligible(MemoryMessage message) =>
        string.Equals(message.Version, messagingOptions.Value.Version, StringComparison.Ordinal)
        && message.StatusName is not (StatusName.Succeeded or StatusName.Failed)
        && message.InlineAttempts == 0
        && message.Retries == 0
        && message.NextRetryAt is null;

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

            if (!_IsRevocationEligible(message))
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
