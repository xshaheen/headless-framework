// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features.SqlServer;

public interface IFeaturesStorageInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
