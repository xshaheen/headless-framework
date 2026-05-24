// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions.PostgreSql;

public interface IPermissionsStorageInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
