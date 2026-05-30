// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Features;

/// <summary>Setup-time extension hook for features storage provider packages.</summary>
[PublicAPI]
public interface IFeaturesStorageOptionsExtension
{
    void AddServices(IServiceCollection services);
}
