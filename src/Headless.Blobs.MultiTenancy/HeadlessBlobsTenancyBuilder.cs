// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Blobs;

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
    /// Fails closed: an operation with no ambient tenant throws <see cref="MissingTenantContextException"/>, and a
    /// tenant id that cannot be one storage segment is refused with <see cref="InvalidOperationException"/>. Blobs
    /// every tenant shares belong in a named store listed in <see cref="TenantBlobScopingOptions.UnscopedStores"/>.
    /// A scoped store implements <see cref="IScopedBlobStorage"/>, whose <see cref="IScopedBlobStorage.Unscoped"/>
    /// addresses physical locations.
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

        if (!services.Any(static d => d.ServiceType == typeof(TenantBlobScopingState)))
        {
            var state = new TenantBlobScopingState(new BlobStorageDecoration(_Wrap));
            services.AddSingleton(state);
            services.DecorateHeadlessBlobs(state.Decoration);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, BlobsTenantScopingStartupValidator>()
        );
        _builder.RecordSeam(Seam, TenantPostureStatus.Guarded, ScopeByTenantCapability);

        return this;
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
            provider.GetRequiredService<ICurrentTenant>()
        );

        return inner is IPresignedUrlBlobStorage
            ? new TenantScopedPresignedBlobStorage(inner, scope)
            : new TenantScopedBlobStorage(inner, scope);
    }
}
