// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Abstractions;
using Headless.EntityFramework.Contexts.Runtime;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// Framework-internal parameter bag resolved as a scoped service and injected into
/// <see cref="HeadlessDbContext"/> via its constructor. Members are intentionally internal because the
/// type only exists to thread the runtime collaborators (tenant, clock, save-changes pipeline) through
/// the DbContext layer without changing public DbContext constructor signatures.
/// </summary>
/// <remarks>
/// Hidden from IntelliSense because consumers should never resolve or inject this type directly. It is
/// not part of the public surface area.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class HeadlessDbContextServices(
    IServiceProvider serviceProvider,
    ICurrentTenant currentTenant,
    IClock clock,
    IHeadlessSaveChangesPipeline saveChangesPipeline,
    IOptions<TenantWriteGuardOptions> tenantWriteGuardOptions
)
{
    internal string? TenantId => currentTenant.Id;

    internal IClock Clock { get; } = clock;

    internal IHeadlessSaveChangesPipeline SaveChangesPipeline { get; } = saveChangesPipeline;

    internal bool IsTenantWriteGuardEnabled => tenantWriteGuardOptions.Value.IsEnabled;

    // The scoped (request) service provider that resolved this bag — the SAME scope the save pipeline captures.
    // Surfaced so coordinated-transaction helpers can enlist with the correct scope for the post-commit drain
    // (EF's CoreOptionsExtension.ApplicationServiceProvider is the ROOT provider and must not be used here).
    internal IServiceProvider ServiceProvider { get; } = serviceProvider;
}
