// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Settings;

/// <summary>Setup-time extension hook for settings storage provider packages. Implementations register provider-specific services into the DI container during the <c>AddHeadlessSettings</c> build phase.</summary>
[PublicAPI]
public interface ISettingsStorageOptionsExtension
{
    /// <summary>Registers the services required by this storage provider extension.</summary>
    /// <param name="services">The application service collection to register into.</param>
    void AddServices(IServiceCollection services);
}
