// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.MultiTenancy;

/// <summary>
/// Resolves the ambient tenant's data placement ahead of the work a pipeline runs for that tenant, so a
/// tenant-routed context injected straight from DI can be built synchronously.
/// </summary>
/// <remarks>
/// <para>
/// A routed context builds its model, and so needs the tenant's schema, inside its constructor, where the async
/// <see cref="ITenantDataPlacementResolver"/> cannot be awaited. The tenancy entry points (the HTTP tenant
/// resolution middlewares, the messaging consume middleware, and the Jobs execute middleware) call
/// <see cref="RunAsync"/> right after they set the ambient tenant. The resolved placement then flows with the
/// async call to everything <c>next</c> runs, across any DI scope the pipeline creates below it.
/// </para>
/// <para>
/// The preloaded placement belongs to the tenant it was resolved for. Once code changes the ambient tenant, a
/// routed context built under the new tenant finds no preloaded placement for it and must come from
/// <c>IDbContextFactory&lt;T&gt;.CreateDbContextAsync</c>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class TenantDataPlacementPreloader(IEnumerable<TenantDataRoutedContextRegistration> routedContexts)
{
    private static readonly AsyncLocal<PreloadedPlacement?> _Current = new();

    private readonly bool _isEnabled = routedContexts.Any();

    /// <summary>
    /// Resolves <paramref name="tenantId"/>'s placement from <paramref name="services"/> and runs
    /// <paramref name="next"/> with it preloaded. Runs <paramref name="next"/> directly when no context is
    /// tenant-routed, when there is no tenant, or when the same tenant's placement is already preloaded.
    /// </summary>
    /// <remarks>
    /// A tenant with no placement is not an error here: only a routed context needs one, and it refuses the tenant
    /// when it is built. A resolver fault is held the same way and rethrown, unchanged, when a routed context is
    /// built, so work that never touches a routed context is not failed by a placement store outage.
    /// Cancellation of <paramref name="cancellationToken"/> propagates immediately.
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
            !_isEnabled
            || string.IsNullOrWhiteSpace(tenantId)
            || string.Equals(_Current.Value?.TenantId, tenantId, StringComparison.Ordinal)
            || services.GetService<ITenantDataPlacementResolver>() is not { } resolver
        )
        {
            await next().ConfigureAwait(false);

            return;
        }

        PreloadedPlacement preloaded;

        try
        {
            var placement = await resolver.ResolveAsync(tenantId, cancellationToken).ConfigureAwait(false);
            preloaded = new(tenantId, placement, Fault: null);
        }
        // Only the pipeline's own cancellation ends the pipeline here. A resolver timeout surfaces as an
        // OperationCanceledException too, and it is a placement fault like any other.
#pragma warning disable CA1031 // Deliberately deferred, not swallowed: the fault is rethrown unchanged when a routed context needs the placement.
        catch (Exception fault)
            when (fault is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            preloaded = new(tenantId, Placement: null, ExceptionDispatchInfo.Capture(fault));
        }

        // Set in this frame so the value flows into next; an async method's AsyncLocal writes never reach its caller.
        _Current.Value = preloaded;
        await next().ConfigureAwait(false);
    }

    /// <summary>
    /// Returns whether a placement was preloaded for <paramref name="tenantId"/> in the current async flow.
    /// </summary>
    /// <param name="tenantId">The tenant a routed context is being built for.</param>
    /// <param name="placement">
    /// The preloaded placement, or <see langword="null"/> when the tenant has none.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the pipeline preloaded <paramref name="tenantId"/>, even when it has no placement;
    /// <see langword="false"/> when nothing was preloaded for it.
    /// </returns>
    /// <exception cref="Exception">The resolver fault captured while preloading, rethrown unchanged.</exception>
    public static bool TryGetPreloaded(string tenantId, out TenantDataPlacement? placement)
    {
        Argument.IsNotNullOrWhiteSpace(tenantId);

        if (
            _Current.Value is not { } preloaded
            || !string.Equals(preloaded.TenantId, tenantId, StringComparison.Ordinal)
        )
        {
            placement = null;

            return false;
        }

        preloaded.Fault?.Throw();
        placement = preloaded.Placement;

        return true;
    }

    private sealed record PreloadedPlacement(
        string TenantId,
        TenantDataPlacement? Placement,
        ExceptionDispatchInfo? Fault
    );
}
