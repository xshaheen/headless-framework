// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Headless.Settings;

internal interface ISettingValueRepositoryForStore
{
    /// <summary>Lets the store refresh the cache once when it shares the repository's cache instance.</summary>
    ISettingValueRecordRepository ForStore(ICache<SettingValueCacheItem> storeCache);
}
