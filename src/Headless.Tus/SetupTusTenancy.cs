// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;
using Headless.Tus.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Tus;

[PublicAPI]
public static class SetupTusTenancy
{
    /// <summary>Configures TUS upload tenant posture through the root Headless tenancy builder.</summary>
    /// <param name="builder">The root tenancy builder.</param>
    /// <param name="configure">The TUS tenancy configuration callback.</param>
    /// <returns>The same root tenancy builder.</returns>
    public static HeadlessTenancyBuilder Tus(
        this HeadlessTenancyBuilder builder,
        Action<HeadlessTusTenancyBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        configure(new HeadlessTusTenancyBuilder(builder));

        return builder;
    }
}

/// <summary>Records tenant posture for Headless TUS uploads.</summary>
[PublicAPI]
public sealed class HeadlessTusTenancyBuilder
{
    /// <summary>The seam name reported in the tenant posture manifest.</summary>
    public const string Seam = "Tus";

    /// <summary>Capability label reported by <see cref="ScopeByTenant"/>.</summary>
    public const string ScopeByTenantCapability = "scope-by-tenant";

    private readonly HeadlessTenancyBuilder _builder;

    internal HeadlessTusTenancyBuilder(HeadlessTenancyBuilder builder)
    {
        _builder = Argument.IsNotNull(builder);
    }

    /// <summary>
    /// Makes every upload created through an upload endpoint belong to a tenant, and makes the store selected
    /// through <c>UseS3</c> keep each tenant's uploads under a path segment of their own.
    /// </summary>
    /// <returns>The same TUS tenancy builder.</returns>
    /// <remarks>
    /// <para>
    /// A request that creates an upload with no ambient tenant, or with a tenant id that cannot be one storage path
    /// segment (<see cref="TenantStorageSegment"/>), is refused with 403 and creates nothing. A request that names
    /// an upload is answered as not found unless it runs in the tenant the upload was created in, as it is without
    /// this call.
    /// </para>
    /// <para>
    /// The call order relative to <c>AddHeadlessTus</c> does not matter. The removal of expired uploads and
    /// <c>resources purge</c> run with no ambient tenant and cover every tenant's uploads. Code that calls a
    /// tenant-scoped store outside a request, such as a job that reads a finished upload, must run inside
    /// <see cref="ICurrentTenant.Change"/> with the upload's tenant; without one the store throws
    /// <see cref="MissingTenantContextException"/>. A store passed to <c>UseStore</c> keeps its own layout: its
    /// uploads are bound to their tenant by the endpoint alone.
    /// </para>
    /// <para>
    /// Turning this on does not move the uploads a store already holds. The S3 store keeps them at their keys
    /// outside any tenant, where a request no longer finds them, and the cleanup removes the incomplete ones once
    /// they expire.
    /// </para>
    /// </remarks>
    public HeadlessTusTenancyBuilder ScopeByTenant()
    {
        var services = _builder.Services;

        // The AsyncLocal-backed defaults, replaced by any real tenancy registration; the same fallback Blobs,
        // Messaging, and Jobs register.
        services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();

        // Options are resolved after every registration ran, so this reaches the upload endpoints whether
        // AddHeadlessTus runs before or after. The sentinel keeps a repeated call from stacking the callback.
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(TusTenantScopingSentinel)))
        {
            services.AddSingleton<TusTenantScopingSentinel>();
            services.PostConfigure<TusTenantScopingOptions>(static options => options.Enabled = true);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, TusTenantScopingStartupValidator>()
        );
        _builder.RecordSeam(Seam, TenantPostureStatus.Guarded, ScopeByTenantCapability);

        return this;
    }
}

/// <summary>Sentinel for the one-shot registration of the callback that turns tenant scoping on.</summary>
internal sealed class TusTenantScopingSentinel;

/// <summary>
/// Emits a startup error when the TUS seam recorded <c>scope-by-tenant</c> but <c>AddHeadlessTus</c> registered no
/// store. The host would otherwise believe its uploads are scoped by tenant while nothing enforces it.
/// </summary>
internal sealed class TusTenantScopingStartupValidator : IHeadlessTenancyValidator
{
    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var recorded =
            context
                .Manifest.GetSeam(HeadlessTusTenancyBuilder.Seam)
                ?.Capabilities.Contains(HeadlessTusTenancyBuilder.ScopeByTenantCapability, StringComparer.Ordinal)
            == true;

        // AddHeadlessTus registers the tenant scope together with the store, and nothing else does.
        if (!recorded || context.Services.GetService<TusTenantScope>() is not null)
        {
            yield break;
        }

        yield return HeadlessTenancyDiagnostic.Error(
            HeadlessTusTenancyBuilder.Seam,
            "HEADLESS_TENANCY_TUS_NO_STORE",
            "Headless TUS seam recorded scope-by-tenant but no TUS store is registered. Select the store through "
                + "AddHeadlessTus(tus => tus.Use…(…)); an ITusStore registered directly is not scoped."
        );
    }
}
