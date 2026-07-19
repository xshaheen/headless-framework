// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Persistence;

namespace Headless.Messaging.Storage.InMemory;

internal sealed class InMemoryStorageInitializer : IStorageInitializer
{
    public string GetPublishedTableName()
    {
        return nameof(InMemoryDataStorage.PublishedMessages);
    }

    public string GetReceivedTableName()
    {
        return nameof(InMemoryDataStorage.ReceivedMessages);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
