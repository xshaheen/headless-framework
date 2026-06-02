// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Settings;

/// <summary>Setup-time extension hook for settings storage provider packages.</summary>
[PublicAPI]
public interface ISettingsStorageOptionsExtension
{
    void AddServices(IServiceCollection services);
}
