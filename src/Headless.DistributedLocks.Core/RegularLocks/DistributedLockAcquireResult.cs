// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

[PublicAPI]
public readonly record struct DistributedLockAcquireResult(bool Acquired, long? FencingToken)
{
    public static DistributedLockAcquireResult Failed => new(Acquired: false, FencingToken: null);
}
