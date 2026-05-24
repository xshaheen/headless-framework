// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features.PostgreSql;

public interface IFeaturesStorageInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
