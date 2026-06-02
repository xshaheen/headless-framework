// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Creates distributed semaphore instances with creation-time capacity binding.</summary>
[PublicAPI]
public interface IDistributedSemaphoreProvider
{
    TimeSpan DefaultTimeUntilExpires { get; }

    TimeSpan DefaultAcquireTimeout { get; }

    /// <summary>Creates a semaphore for <paramref name="resource"/> with a fixed maximum holder count.</summary>
    IDistributedSemaphore CreateSemaphore(string resource, int maxCount);

    /// <summary>Gets the number of currently live holders for <paramref name="resource"/>.</summary>
    Task<long> GetHolderCountAsync(string resource, CancellationToken cancellationToken = default);
}
