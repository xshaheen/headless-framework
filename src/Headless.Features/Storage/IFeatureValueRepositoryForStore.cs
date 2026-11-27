// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Headless.Features;

internal interface IFeatureValueRepositoryForStore
{
    /// <summary>Lets the store refresh the cache once when it shares the repository's cache instance.</summary>
    IFeatureValueRecordRepository ForStore(ICache storeCache);
}
