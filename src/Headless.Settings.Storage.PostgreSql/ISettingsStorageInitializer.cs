// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings.PostgreSql;

public interface ISettingsStorageInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
