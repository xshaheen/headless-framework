// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog.SqlServer;

public interface IAuditLogStorageInitializer
{
    bool IsInitialized { get; }

    Task WaitForInitializationAsync(CancellationToken cancellationToken = default);

    Task InitializeAsync(CancellationToken cancellationToken = default);
}
