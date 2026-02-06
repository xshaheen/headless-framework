// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Persistence;

public interface IStorageInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    string GetPublishedTableName();

    string GetReceivedTableName();

    string GetLockTableName();

    string GetScheduledJobsTableName();

    string GetJobExecutionsTableName();
}
