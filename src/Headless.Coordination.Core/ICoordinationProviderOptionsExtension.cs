// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Coordination;

/// <summary>Setup-time extension hook for coordination provider packages.</summary>
[PublicAPI]
public interface ICoordinationProviderOptionsExtension
{
    void AddServices(IServiceCollection services);
}
