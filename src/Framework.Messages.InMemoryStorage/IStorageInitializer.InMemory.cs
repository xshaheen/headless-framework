// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Persistence;

namespace Framework.Messages;

internal class InMemoryStorageInitializer : IStorageInitializer
{
    public string GetPublishedTableName()
    {
        return nameof(InMemoryStorage.PublishedMessages);
    }

    public string GetReceivedTableName()
    {
        return nameof(InMemoryStorage.ReceivedMessages);
    }

    public string GetLockTableName()
    {
        return string.Empty;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
