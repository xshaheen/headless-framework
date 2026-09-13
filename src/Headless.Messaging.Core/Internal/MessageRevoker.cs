// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Persistence;

namespace Headless.Messaging.Internal;

internal sealed class MessageRevoker(IDataStorage storage) : IMessageRevoker
{
    public ValueTask<MessageRevocationResult> RevokeAsync(Guid storageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return storage is IMessageRevocationStorage revocation
            ? revocation.RevokeAsync(storageId, cancellationToken)
            : throw new NotSupportedException(
                $"Messaging storage provider '{storage.GetType().FullName}' does not support message revocation."
            );
    }
}
