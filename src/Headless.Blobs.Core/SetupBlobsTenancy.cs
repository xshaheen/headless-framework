// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Blobs;

[PublicAPI]
public static class SetupBlobsTenancy
{
    /// <summary>Configures blob storage tenant posture through the root Headless tenancy builder.</summary>
    /// <param name="builder">The root tenancy builder.</param>
    /// <param name="configure">The blob tenancy configuration callback.</param>
    /// <returns>The same root tenancy builder.</returns>
    public static HeadlessTenancyBuilder Blobs(
        this HeadlessTenancyBuilder builder,
        Action<HeadlessBlobsTenancyBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        configure(new HeadlessBlobsTenancyBuilder(builder));

        return builder;
    }

    /// <summary>
    /// Wraps every <see cref="IBlobStorage"/> registration with tenant scoping once both the tenancy seam and
    /// <c>AddHeadlessBlobs</c> have run, whichever runs last. Scoping is applied after the blob setup's cross-cutting
    /// extensions, so it is the outermost decorator and rewrites a location before any capability decorator (the
    /// signed-URL endpoint) sees it.
    /// </summary>
    internal static void ApplyTenantScoping(IServiceCollection services)
    {
        var state = services
            .Where(static d => d.ServiceType == typeof(TenantBlobScopingState))
            .Select(static d => d.ImplementationInstance)
            .OfType<TenantBlobScopingState>()
            .FirstOrDefault();

        if (state?.Applied != false)
        {
            return;
        }

        state.Applied = true;

        if (services.TryDecorate<IBlobStorage>((inner, provider) => _Wrap(inner, store: null, provider)))
        {
            state.ScopedRegistrations++;
        }

        var storeNames = services
            .Where(static d => d.IsKeyedService && d.ServiceType == typeof(IBlobStorage))
            .Select(static d => d.ServiceKey)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Keyed IPresignedUrlBlobStorage forwards resolve the keyed IBlobStorage, so they reach the scoped wrapper
        // without being decorated themselves.
        foreach (var name in storeNames)
        {
            if (services.TryDecorateKeyed<IBlobStorage>(name, (inner, provider) => _Wrap(inner, name, provider)))
            {
                state.ScopedRegistrations++;
            }
        }
    }

    private static IBlobStorage _Wrap(IBlobStorage inner, string? store, IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<TenantBlobScopingOptions>>().Value;

        if (store is not null && options.UnscopedStores.Contains(store))
        {
            return inner;
        }

        var scope = new TenantBlobScope(
            options.Strategy,
            options.ContainerPrefix,
            provider.GetRequiredService<ICurrentTenant>(),
            provider.GetRequiredService<ITenantStorageScopeBypass>()
        );

        return inner is IPresignedUrlBlobStorage
            ? new TenantScopedPresignedBlobStorage(inner, scope)
            : new TenantScopedBlobStorage(inner, scope);
    }
}

/// <summary>Records tenant posture for Headless blob storage.</summary>
[PublicAPI]
public sealed class HeadlessBlobsTenancyBuilder
{
    /// <summary>The seam name reported in the tenant posture manifest.</summary>
    public const string Seam = "Blobs";

    /// <summary>Capability label reported by <see cref="ScopeByTenant()"/>.</summary>
    public const string ScopeByTenantCapability = "scope-by-tenant";

    private readonly HeadlessTenancyBuilder _builder;

    internal HeadlessBlobsTenancyBuilder(HeadlessTenancyBuilder builder)
    {
        _builder = Argument.IsNotNull(builder);
    }

    /// <summary>
    /// Scopes every blob store registered through <c>AddHeadlessBlobs</c> by the ambient tenant, with the tenant id
    /// as a leading path segment inside the caller's container.
    /// </summary>
    /// <returns>The same blob tenancy builder.</returns>
    /// <remarks>See <see cref="ScopeByTenant(Action{TenantBlobScopingOptions})"/>.</remarks>
    public HeadlessBlobsTenancyBuilder ScopeByTenant()
    {
        return ScopeByTenant(static _ => { });
    }

    /// <summary>
    /// Scopes every blob store registered through <c>AddHeadlessBlobs</c> by the ambient tenant. Every location, list
    /// query, and delete-all query is rewritten before it reaches the provider, including presigned URL requests, and
    /// every returned key is rewritten back, so callers keep addressing logical locations.
    /// </summary>
    /// <param name="configure">Selects the strategy and the stores left unscoped.</param>
    /// <returns>The same blob tenancy builder.</returns>
    /// <remarks>
    /// <para>
    /// Fails closed: an operation with no ambient tenant throws <see cref="MissingTenantContextException"/> unless
    /// <see cref="ITenantStorageScopeBypass"/> is active, in which case it passes through unchanged. A tenant id that
    /// cannot be one storage segment is refused with <see cref="InvalidOperationException"/>.
    /// </para>
    /// <para>
    /// The call order relative to <c>AddHeadlessBlobs</c> does not matter. Stores registered outside
    /// <c>AddHeadlessBlobs</c> are not scoped, and <see cref="IBlobContainerManager"/> is never scoped.
    /// </para>
    /// </remarks>
    public HeadlessBlobsTenancyBuilder ScopeByTenant(Action<TenantBlobScopingOptions> configure)
    {
        Argument.IsNotNull(configure);

        var services = _builder.Services;

        services.Configure<TenantBlobScopingOptions, TenantBlobScopingOptionsValidator>(configure);

        // The AsyncLocal-backed defaults, replaced by any real tenancy registration; the same fallback Permissions,
        // Messaging, and Jobs register.
        services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();
        services.TryAddSingleton<ITenantStorageScopeBypass>(TenantStorageScopeBypass.Instance);

        if (!services.Any(static d => d.ServiceType == typeof(TenantBlobScopingState)))
        {
            services.AddSingleton(new TenantBlobScopingState());
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, BlobsTenantScopingStartupValidator>()
        );
        _builder.RecordSeam(Seam, TenantPostureStatus.Guarded, ScopeByTenantCapability);

        // When AddHeadlessBlobs already ran, its stores are registered and are scoped now; otherwise AddHeadlessBlobs
        // applies the scoping itself once its providers are registered.
        if (SetupBlobsCore.IsRegistered(services))
        {
            SetupBlobsTenancy.ApplyTenantScoping(services);
        }

        return this;
    }
}

/// <summary>Registration-time record of whether blob stores were wrapped with tenant scoping.</summary>
internal sealed class TenantBlobScopingState
{
    public bool Applied { get; set; }

    public int ScopedRegistrations { get; set; }
}

/// <summary>
/// Emits a startup error when the blob seam recorded <c>scope-by-tenant</c> but no store was wrapped, typically
/// because no store was registered through <c>AddHeadlessBlobs</c>. The host would otherwise believe its blobs are
/// tenant-scoped while every call reaches an unscoped store.
/// </summary>
internal sealed class BlobsTenantScopingStartupValidator(TenantBlobScopingState state) : IHeadlessTenancyValidator
{
    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var recorded =
            context
                .Manifest.GetSeam(HeadlessBlobsTenancyBuilder.Seam)
                ?.Capabilities.Contains(HeadlessBlobsTenancyBuilder.ScopeByTenantCapability, StringComparer.Ordinal)
            == true;

        if (!recorded || state.ScopedRegistrations > 0)
        {
            yield break;
        }

        yield return HeadlessTenancyDiagnostic.Error(
            HeadlessBlobsTenancyBuilder.Seam,
            "HEADLESS_TENANCY_BLOBS_NO_SCOPED_STORE",
            "Headless blob seam recorded scope-by-tenant but no blob store was scoped. Register the stores through "
                + "AddHeadlessBlobs(...); a store registered directly as IBlobStorage is never scoped."
        );
    }
}
