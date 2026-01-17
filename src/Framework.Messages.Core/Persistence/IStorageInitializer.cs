// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages.Persistence;

public interface IStorageInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    string GetPublishedTableName();

    string GetReceivedTableName();

    string GetLockTableName();
}
