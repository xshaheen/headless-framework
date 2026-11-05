// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// A feature module of the Headless dashboard: the data and UI one feature adds under the shell's base path.
/// </summary>
/// <remarks>
/// <para>
/// A module contributes data, and the shell renders it. This interface carries only what every module has: its id,
/// display name, embedded UI assets, and its own API endpoints. Everything else a module can offer is an optional
/// capability interface implemented on the same class and discovered by a type check:
/// <see cref="IDashboardAttentionSource"/>, <see cref="IDashboardOverviewSource"/>,
/// <see cref="IDashboardObjectResolver"/>, <see cref="IDashboardTraceSource"/>, <see cref="IDashboardRelatedSource"/>,
/// <see cref="IDashboardPayloadSource"/>, <see cref="IDashboardPayloadFormatter"/>, <see cref="IDashboardCommandSource"/>,
/// <see cref="IDashboardOperationRunner"/>, and <see cref="IDashboardChangeSource"/>.
/// </para>
/// <para>
/// Register a module through <see cref="HeadlessDashboardSetupBuilder.AddModule{TModule, TOptions}"/>, which a module
/// package wraps in its own <c>Add{Module}</c> extension. The shell resolves the module from the container as a
/// singleton, so its members must be thread-safe. No member may open a connection or run a query while the host
/// starts: the shell reads <see cref="Id"/>, <see cref="DisplayName"/>, and <see cref="Assets"/> once when it builds
/// its manifest, and those are in-process reads.
/// </para>
/// <para>
/// Module packages implement this interface. It grows by new capability interfaces, or by default interface members
/// when a correct default can be written; it never gains a parameter on an existing member.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardModule
{
    /// <summary>
    /// Gets the module's id: its URL segment, its API group at <c>{base}/api/{id}</c>, and its stream topic prefix.
    /// It must equal the id the module was registered under: a lower-case kebab token such as <c>jobs</c>.
    /// </summary>
    string Id { get; }

    /// <summary>Gets the name the shell shows for the module, such as <c>Jobs</c>.</summary>
    string DisplayName { get; }

    /// <summary>Gets the module's embedded UI build: its entry, chunks, styles, and <c>manifest.json</c>.</summary>
    DashboardModuleAssets Assets { get; }

    /// <summary>
    /// Maps the module's own API endpoints. The shell mounts them at <c>{base}/api/{id}</c> behind the module's view
    /// policy; mark each state-changing endpoint with <see cref="DashboardModuleEndpoints.RequireOperate{TBuilder}"/>.
    /// </summary>
    /// <param name="endpoints">The module's API group.</param>
    void MapEndpoints(DashboardModuleEndpoints endpoints);
}
