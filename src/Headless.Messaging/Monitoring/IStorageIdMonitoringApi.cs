// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>Loads dashboard row projections without reading or deserializing the stored message bodies.</summary>
internal interface IStorageIdMonitoringApi
{
    ValueTask<IReadOnlyList<StoredMessageView>> GetMessageViewsAsync(
        MessageType type,
        IReadOnlyList<Guid> storageIds,
        CancellationToken cancellationToken
    );
}

internal readonly record struct StoredMessageView(MessageView View, string? ExceptionInfo);
