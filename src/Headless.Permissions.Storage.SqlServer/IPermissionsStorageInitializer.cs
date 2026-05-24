// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions.SqlServer;

public interface IPermissionsStorageInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
