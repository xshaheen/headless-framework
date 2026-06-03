// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

public interface IFencingTokenSource
{
    ValueTask<long?> NextAsync(string resource, CancellationToken cancellationToken = default);
}
