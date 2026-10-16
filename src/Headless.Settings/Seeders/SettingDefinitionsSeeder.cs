// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Settings;

/// <summary>
/// Writes the code-declared setting definitions into the dynamic store when
/// <see cref="SettingManagementOptions.SaveStaticSettingsToDatabase"/> is <see langword="true"/>.
/// </summary>
internal sealed class SettingDefinitionsSeeder(
    IServiceProvider services,
    IOptions<SettingManagementOptions> optionsAccessor,
    ILogger<SettingDefinitionsSeeder> logger,
    TimeProvider retryClock
) : DefinitionStoreSeeder(logger, retryClock)
{
    public override string Name => "SettingDefinitions";

    protected override bool IsEnabled => optionsAccessor.Value.SaveStaticSettingsToDatabase;

    protected override Task SaveAsync(CancellationToken cancellationToken) =>
        services.GetRequiredService<IDynamicSettingDefinitionStore>().SaveAsync(cancellationToken);
}
