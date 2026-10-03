// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Contexts.Runtime;
using Headless.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.EntityFramework;

/// <summary>
/// Capability seam shared by the Headless DbContext bases (<see cref="HeadlessDbContext"/> and the Identity
/// context) so the runtime, save pipeline, factory, and disposal infrastructure operate against either base
/// without a common class — the Identity context must derive from <c>IdentityDbContext</c>, so this interface
/// is the only shared seam. Exposes the tenant and schema the runtime reads and the service provider the context's
/// collaborators resolve from. Implemented explicitly by both bases, so it does not widen their public surface; it is
/// public so capability-based extensions can target any Headless-managed context.
/// </summary>
[PublicAPI]
public interface IHeadlessDbContext
{
    /// <summary>
    /// Optional database schema applied to all entities that do not declare an explicit schema.
    /// <see langword="null"/> leaves the provider default in place.
    /// </summary>
    string? DefaultSchema { get; }

    /// <summary>
    /// The identifier of the tenant whose data this context is scoped to, or <see langword="null"/>
    /// when running in a host/admin context outside a tenant scope.
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    /// The service provider the context's scoped collaborators resolve from: the DI scope that resolved the context,
    /// or, for a context created outside any scope (a factory, or <see langword="new"/>), a private scope opened on first use and
    /// disposed with the context. EF's <c>ApplicationServiceProvider</c> is the root provider and would hand back a
    /// different scoped instance, or throw for a scoped registration.
    /// </summary>
    IServiceProvider ServiceProvider { get; }
}

/// <summary>
/// Internal seam to the per-context runtime, implemented explicitly by both Headless context bases so the factory,
/// the DI registrations, and the query filters reach the lease binding without widening the public surface.
/// </summary>
internal interface IHeadlessDbContextRuntimeOwner
{
    HeadlessDbContextRuntime Runtime { get; }
}
