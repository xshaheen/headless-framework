// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings.SqlServer;

public interface ISettingsStorageInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
