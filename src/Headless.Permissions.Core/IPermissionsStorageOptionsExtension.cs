// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Permissions;

/// <summary>Setup-time extension hook for permissions storage provider packages.</summary>
[PublicAPI]
public interface IPermissionsStorageOptionsExtension
{
    void AddServices(IServiceCollection services);
}
