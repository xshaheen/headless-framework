// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.DistributedLocks;

/// <summary>
/// Returned by <see cref="SetupDistributedLocks"/> after the distributed-locks provider is
/// registered. Exposes the underlying <see cref="IServiceCollection"/> so callers can chain
/// additional registrations against the same container.
/// </summary>
[PublicAPI]
public sealed class HeadlessDistributedLocksBuilder(IServiceCollection services)
{
    /// <summary>The service collection that distributed-locks registrations were added to.</summary>
    public IServiceCollection Services { get; } = services;
}
