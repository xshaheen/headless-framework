// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Abstractions;
using Headless.EntityFramework.Contexts.Runtime;
using Headless.MultiTenancy;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// Constructor parameter bag resolved as a scoped service and injected into <see cref="HeadlessDbContext"/>.
/// Consumers pass this type through derived context constructors, but its members are framework-internal.
/// </summary>
/// <remarks>
/// Hidden from IntelliSense because consumers should only accept this type in derived DbContext constructors;
/// they should not resolve it directly or depend on its member shape.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class HeadlessDbContextServices(
    IServiceProvider serviceProvider,
    ICurrentTenant currentTenant,
    IHeadlessSaveChangesPipeline saveChangesPipeline,
    IOptions<TenantWriteGuardOptions> tenantWriteGuardOptions
)
{
    internal string? TenantId => currentTenant.Id;

    internal IHeadlessSaveChangesPipeline SaveChangesPipeline { get; } = saveChangesPipeline;

    internal bool IsTenantWriteGuardEnabled => tenantWriteGuardOptions.Value.IsEnabled;

    // The scoped (request) service provider that resolved this bag — the SAME scope the save pipeline captures.
    // Held so the runtime (ITenantWriteGuardBypass on every Added transition) and anything reaching the context
    // through IHeadlessDbContext resolve scoped services from the scope that owns this context: EF's
    // CoreOptionsExtension.ApplicationServiceProvider is the ROOT provider and would hand back a different
    // instance, or throw for a scoped registration.
    internal IServiceProvider ServiceProvider { get; } = serviceProvider;
}
