// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Permissions;

/// <summary>Setup-time extension hook for permissions storage provider packages.</summary>
[PublicAPI]
public interface IPermissionsStorageOptionsExtension
{
    /// <summary>
    /// Registers the storage provider's services (repositories, initializers, etc.) into
    /// <paramref name="services"/>. Called once by the setup pipeline after all extensions are collected.
    /// </summary>
    void AddServices(IServiceCollection services);
}
