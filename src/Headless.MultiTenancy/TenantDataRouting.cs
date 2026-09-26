// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.MultiTenancy;

/// <summary>
/// Records that a data context type is tenant-routed: each instance connects to its tenant's schema or database
/// from <see cref="ITenantDataPlacementResolver"/>. Registered as a singleton instance by the data-access
/// package's routing entry point (for example <c>RouteTenantData&lt;TContext&gt;()</c>) so packages that must never
/// run over a routed context can detect it without referencing that package.
/// </summary>
/// <param name="contextType">The routed data context type.</param>
[PublicAPI]
public sealed class TenantDataRoutedContextRegistration(Type contextType)
{
    /// <summary>The routed data context type.</summary>
    public Type ContextType { get; } = Argument.IsNotNull(contextType);
}

/// <summary>Registration helpers for features that must run over a context that is not tenant-routed.</summary>
[PublicAPI]
public static class HeadlessTenantDataRoutingRequirements
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Declares that <paramref name="owner"/> runs over <paramref name="contextType"/> and that the context must
        /// not be tenant-routed, and fails tenancy startup validation when it is. Relays and pollers (outbox storage,
        /// the Jobs store) read one fixed database, so rows written through a routed context would be stranded where
        /// they never look; host-level stores (tenant catalog, Settings, Features, Permissions) would split their
        /// state across tenant stores behind one shared cache.
        /// </summary>
        /// <param name="contextType">The data context the feature runs over.</param>
        /// <param name="owner">The feature, named in the startup error (for example <c>"Jobs UseApplicationDbContext"</c>).</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="contextType"/> or <paramref name="owner"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="owner"/> is empty or white space.</exception>
        public IServiceCollection RequireUnroutedTenantDataContext(Type contextType, string owner)
        {
            Argument.IsNotNull(services);
            Argument.IsNotNull(contextType);
            Argument.IsNotNullOrWhiteSpace(owner);

            services.AddSingleton(new UnroutedTenantDataContextRequirement(contextType, owner));
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHeadlessTenancyValidator, UnroutedTenantDataContextValidator>()
            );

            return services;
        }
    }
}

/// <summary>A feature's requirement that the context it runs over is not tenant-routed.</summary>
internal sealed record UnroutedTenantDataContextRequirement(Type ContextType, string Owner);

/// <summary>Fails startup when a context a feature requires to stay unrouted is tenant-routed.</summary>
internal sealed class UnroutedTenantDataContextValidator(
    IEnumerable<UnroutedTenantDataContextRequirement> requirements,
    IEnumerable<TenantDataRoutedContextRegistration> routedContexts
) : IHeadlessTenancyValidator
{
    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var routed = routedContexts.Select(static r => r.ContextType).ToHashSet();

        foreach (var requirement in requirements.Where(r => routed.Contains(r.ContextType)))
        {
            yield return HeadlessTenancyDiagnostic.Error(
                SetupHeadlessTenancyDataPlacement.Seam,
                "HEADLESS_TENANCY_ROUTED_CONTEXT_NOT_ALLOWED",
                $"{requirement.Owner} runs over '{requirement.ContextType.Name}', which is tenant-routed "
                    + "(RouteTenantData). Relays, pollers, and host-level stores need one fixed database. Give "
                    + "them a context that is not tenant-routed."
            );
        }
    }
}
