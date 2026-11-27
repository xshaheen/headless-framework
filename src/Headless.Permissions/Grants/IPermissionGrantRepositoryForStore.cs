// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Headless.Permissions;

internal interface IPermissionGrantRepositoryForStore
{
    /// <summary>Lets the store refresh the cache once when it shares the repository's cache instance.</summary>
    IPermissionGrantRepository ForStore(ICache<PermissionGrantCacheItem> storeCache);
}
