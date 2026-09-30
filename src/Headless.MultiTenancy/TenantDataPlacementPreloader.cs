// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Runtime.ExceptionServices;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.MultiTenancy;

/// <summary>
/// Resolves the ambient tenant's data placements ahead of the work a pipeline runs for that tenant, one per routed
/// data store, so a tenant-routed context injected straight from DI can be built synchronously.
/// </summary>
/// <remarks>
/// <para>
/// A routed context builds its model, and so needs the tenant's schema, inside its constructor, where the async
/// <see cref="ITenantDataPlacementResolver"/> cannot be awaited. The tenancy entry points (the HTTP tenant
/// resolution middlewares, the messaging consume middleware, and the Jobs execute middleware) call
/// <see cref="RunAsync"/> right after they set the ambient tenant. The resolved placements then flow with the
/// async call to everything <c>next</c> runs, across any DI scope the pipeline creates below it.
/// </para>
/// <para>
/// The preloaded placements belong to the tenant they were resolved for. Once code changes the ambient tenant, a
/// routed context built under the new tenant finds no preloaded placement for it and must come from
/// <c>IDbContextFactory&lt;T&gt;.CreateDbContextAsync</c>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class TenantDataPlacementPreloader
{
    private static readonly AsyncLocal<PreloadedPlacements?> _Current = new();

    private readonly FrozenSet<string> _dataStores;

    /// <summary>Initializes the preloader for the routed contexts registered on the host.</summary>
    /// <param name="routedContexts">The routed context registrations; their distinct data stores are preloaded.</param>
    public TenantDataPlacementPreloader(IEnumerable<TenantDataRoutedContextRegistration> routedContexts)
    {
        Argument.IsNotNull(routedContexts);

        _dataStores = routedContexts.Select(static routed => routed.DataStore).ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Resolves <paramref name="tenantId"/>'s placement for every routed data store from <paramref name="services"/>
    /// and runs <paramref name="next"/> with them preloaded. Runs <paramref name="next"/> directly when no context
    /// is tenant-routed, when there is no tenant, or when the same tenant's placements are already preloaded.
    /// </summary>
    /// <remarks>
    /// A tenant with no placement for a store is not an error here: only a routed context needs one, and it
    /// refuses the tenant when it is built. A resolver fault is held the same way, per store, and rethrown
    /// unchanged when a routed context of that store is built, so work that never touches a routed context is not
    /// failed by a placement store outage. Cancellation of <paramref name="cancellationToken"/> propagates
    /// immediately.
    /// </remarks>
    /// <param name="services">The pipeline's scoped service provider, which owns the placement resolver.</param>
    /// <param name="tenantId">The ambient tenant id the pipeline just established.</param>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="cancellationToken">The pipeline's cancellation token.</param>
    public async Task RunAsync(
        IServiceProvider services,
        string? tenantId,
        Func<Task> next,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(next);

        if (
            _dataStores.Count == 0
            || string.IsNullOrWhiteSpace(tenantId)
            || string.Equals(_Current.Value?.TenantId, tenantId, StringComparison.Ordinal)
            || services.GetService<ITenantDataPlacementResolver>() is not { } resolver
        )
        {
            await next().ConfigureAwait(false);

            return;
        }

        var placements = new Dictionary<string, PreloadedPlacement>(_dataStores.Count, StringComparer.Ordinal);

        foreach (var dataStore in _dataStores)
        {
            placements[dataStore] = await _ResolveAsync(resolver, new(tenantId, dataStore), cancellationToken)
                .ConfigureAwait(false);
        }

        // Set in this frame so the value flows into next; an async method's AsyncLocal writes never reach its caller.
        _Current.Value = new PreloadedPlacements(tenantId, placements.ToFrozenDictionary(StringComparer.Ordinal));
        await next().ConfigureAwait(false);
    }

    /// <summary>
    /// Returns whether a placement was preloaded for <paramref name="request"/> in the current async flow.
    /// </summary>
    /// <param name="request">The tenant and data store a routed context is being built for.</param>
    /// <param name="placement">
    /// The preloaded placement, or <see langword="null"/> when the tenant has none for that data store.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the pipeline preloaded <paramref name="request"/>, even when the tenant has no
    /// placement for it; <see langword="false"/> when nothing was preloaded for it.
    /// </returns>
    /// <exception cref="Exception">The resolver fault captured while preloading, rethrown unchanged.</exception>
    public static bool TryGetPreloaded(TenantDataPlacementRequest request, out TenantDataPlacement? placement)
    {
        Argument.IsNotNull(request);

        if (
            _Current.Value is not { } preloaded
            || !string.Equals(preloaded.TenantId, request.TenantId, StringComparison.Ordinal)
            || !preloaded.Placements.TryGetValue(request.DataStore, out var entry)
        )
        {
            placement = null;

            return false;
        }

        entry.Fault?.Throw();
        placement = entry.Placement;

        return true;
    }

    /// <summary>
    /// Returns whether a placement was preloaded for <paramref name="tenantId"/>'s
    /// <see cref="TenantDataPlacementRequest.DefaultDataStore"/> in the current async flow.
    /// </summary>
    /// <inheritdoc cref="TryGetPreloaded(TenantDataPlacementRequest, out TenantDataPlacement?)"/>
    /// <param name="tenantId">The tenant a routed context is being built for.</param>
    /// <param name="placement">The preloaded placement, or <see langword="null"/> when the tenant has none.</param>
    public static bool TryGetPreloaded(string tenantId, out TenantDataPlacement? placement)
    {
        return TryGetPreloaded(new TenantDataPlacementRequest(tenantId), out placement);
    }

    private static async Task<PreloadedPlacement> _ResolveAsync(
        ITenantDataPlacementResolver resolver,
        TenantDataPlacementRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return new(await resolver.ResolveAsync(request, cancellationToken).ConfigureAwait(false), Fault: null);
        }
        // Only the pipeline's own cancellation ends the pipeline here. A resolver timeout surfaces as an
        // OperationCanceledException too, and it is a placement fault like any other.
#pragma warning disable CA1031 // Deliberately deferred, not swallowed: the fault is rethrown unchanged when a routed context needs the placement.
        catch (Exception fault)
            when (fault is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            return new(Placement: null, ExceptionDispatchInfo.Capture(fault));
        }
    }

    private sealed record PreloadedPlacements(string TenantId, FrozenDictionary<string, PreloadedPlacement> Placements);

    private sealed record PreloadedPlacement(TenantDataPlacement? Placement, ExceptionDispatchInfo? Fault);
}
