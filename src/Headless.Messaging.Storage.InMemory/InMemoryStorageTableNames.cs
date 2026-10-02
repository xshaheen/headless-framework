// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Persistence;

namespace Headless.Messaging.Storage.InMemory;

internal sealed class InMemoryStorageTableNames : IStorageTableNames
{
    public string GetPublishedTableName()
    {
        return nameof(InMemoryDataStorage.PublishedMessages);
    }

    public string GetReceivedTableName()
    {
        return nameof(InMemoryDataStorage.ReceivedMessages);
    }
}
