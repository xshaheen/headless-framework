// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Permissions;

/// <summary>
/// Writes the code-declared permission definitions into the dynamic store when
/// <see cref="PermissionManagementOptions.SaveStaticPermissionsToDatabase"/> is <see langword="true"/>.
/// </summary>
internal sealed class PermissionDefinitionsSeeder(
    IServiceProvider services,
    IOptions<PermissionManagementOptions> optionsAccessor,
    ILogger<PermissionDefinitionsSeeder> logger,
    TimeProvider retryClock
) : DefinitionStoreSeeder(logger, retryClock)
{
    public override string Name => "PermissionDefinitions";

    protected override bool IsEnabled => optionsAccessor.Value.SaveStaticPermissionsToDatabase;

    protected override Task SaveAsync(CancellationToken cancellationToken) =>
        services.GetRequiredService<IDynamicPermissionDefinitionStore>().SaveAsync(cancellationToken);
}
