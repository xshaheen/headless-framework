// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Hosting.Storage;

/// <summary>Setup-time extension hook for storage provider packages.</summary>
public interface IStorageOptionsExtension
{
    void AddServices(IServiceCollection services);
}
