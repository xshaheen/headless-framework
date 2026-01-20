// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Persistence;

namespace Framework.Messages;

internal class InMemoryStorageInitializer : IStorageInitializer
{
    public string GetPublishedTableName()
    {
        return nameof(InMemoryDataStorage.PublishedMessages);
    }

    public string GetReceivedTableName()
    {
        return nameof(InMemoryDataStorage.ReceivedMessages);
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
