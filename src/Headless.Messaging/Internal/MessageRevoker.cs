// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Persistence;

namespace Headless.Messaging.Internal;

internal sealed class MessageRevoker(IDataStorage storage, MessagingOutboxes? outboxes = null) : IMessageRevoker
{
    public async ValueTask<MessageRevocationResult> RevokeAsync(
        Guid storageId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _Revocation(storage).RevokeAsync(storageId, cancellationToken).ConfigureAwait(false);

        // A handle from a publish enlisted in an additional outbox's unit names a row in that outbox's database.
        // Storage ids are globally unique, so the first outbox that knows the row answers for it.
        foreach (var outbox in outboxes?.Secondaries ?? [])
        {
            if (result is not MessageRevocationResult.NotFound)
            {
                break;
            }

            result = await _Revocation(outbox.Storage).RevokeAsync(storageId, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private static IMessageRevocationStorage _Revocation(IDataStorage target)
    {
        return target as IMessageRevocationStorage
            ?? throw new NotSupportedException(
                $"Messaging storage provider '{target.GetType().FullName}' does not support message revocation."
            );
    }
}
