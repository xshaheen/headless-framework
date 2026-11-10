// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Headless.Dashboard;

/// <summary>
/// The API group a module maps its own endpoints on, at <c>{base}/api/{id}</c>, behind the module's view policy.
/// </summary>
[PublicAPI]
public sealed class DashboardModuleEndpoints
{
    internal DashboardModuleEndpoints(string moduleId, IEndpointRouteBuilder routes)
    {
        ModuleId = Argument.IsNotNullOrWhiteSpace(moduleId);
        Routes = Argument.IsNotNull(routes);
    }

    /// <summary>Gets the id of the module whose group this is.</summary>
    public string ModuleId { get; }

    /// <summary>Gets the route builder to map the module's endpoints on, relative to <c>{base}/api/{id}</c>.</summary>
    public IEndpointRouteBuilder Routes { get; }

    /// <summary>
    /// Marks <paramref name="endpoint"/> as a state-changing operator action, so the shell also requires the module's
    /// operate policy for it.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder, such as a route handler.</typeparam>
    /// <param name="endpoint">The operator-action endpoint.</param>
    /// <returns>The same <paramref name="endpoint"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <see langword="null"/>.</exception>
    public TBuilder RequireOperate<TBuilder>(TBuilder endpoint)
        where TBuilder : IEndpointConventionBuilder
    {
        Argument.IsNotNull(endpoint);

        endpoint.WithMetadata(new DashboardOperateMetadata(ModuleId));

        return endpoint;
    }
}

/// <summary>
/// Endpoint metadata that marks a module endpoint as an operator action. The shell's module group reads it and adds
/// the operate policy of the module it names.
/// </summary>
/// <param name="ModuleId">The module whose operate policy the endpoint requires.</param>
internal sealed record DashboardOperateMetadata(string ModuleId);
