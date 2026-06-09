// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.DistributedLocks;

/// <summary>Setup-time extension hook for distributed-lock provider packages.</summary>
[PublicAPI]
public interface IDistributedLocksOptionsExtension
{
    void AddServices(IServiceCollection services);
}
