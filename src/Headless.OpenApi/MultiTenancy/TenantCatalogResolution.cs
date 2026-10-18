// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.OpenApi;

/// <summary>
/// What pre-authentication tenant catalog resolution does to the requests of this application: the identifier sources
/// it consults and whether its rejections name the exact reason.
/// </summary>
/// <remarks>
/// Resolution is configured when the tenant posture records the catalog resolution capability and at least one
/// identifier source is registered, the state <c>UseHeadlessTenantCatalogResolution()</c> requires before it adds the
/// middleware. It is read from registrations rather than from the pipeline marker, so a document generated without
/// running the request pipeline, such as at build time, describes the same contract.
/// </remarks>
internal sealed class TenantCatalogResolution
{
    private TenantCatalogResolution(
        bool detailedErrors,
        IReadOnlyList<string> headerNames,
        IReadOnlyList<string> hostTemplates,
        bool hasOtherSources
    )
    {
        DetailedErrors = detailedErrors;
        HeaderNames = headerNames;
        HostTemplates = hostTemplates;
        HasOtherSources = hasOtherSources;
    }

    /// <summary>Whether rejections carry the granular codes (<see cref="TenantCatalogOptions.DetailedResolutionErrors"/>).</summary>
    public bool DetailedErrors { get; }

    /// <summary>The distinct request headers the header source reads, empty when no header source is registered.</summary>
    public IReadOnlyList<string> HeaderNames { get; }

    /// <summary>The host templates the host source matches, empty when no host source is registered.</summary>
    public IReadOnlyList<string> HostTemplates { get; }

    /// <summary>Whether a source other than the header source can supply the identifier.</summary>
    public bool HasOtherSources { get; }

    /// <summary>The resolution configured in <paramref name="services"/>, or <see langword="null"/> when none is.</summary>
    public static TenantCatalogResolution? Get(IServiceProvider services)
    {
        var seam = services.GetService<TenantPostureManifest>()?.GetSeam(TenantCatalogPosture.Seam);

        if (seam?.Capabilities.Contains(TenantCatalogPosture.ResolutionCapability, StringComparer.Ordinal) != true)
        {
            return null;
        }

        var sources = services.GetServices<ITenantIdentifierSource>().ToArray();

        if (sources.Length == 0)
        {
            return null;
        }

        var hasHeaderSource = sources.OfType<HeaderTenantIdentifierSource>().Any();
        var hasHostSource = sources.OfType<HostTenantIdentifierSource>().Any();

        return new TenantCatalogResolution(
            services.GetRequiredService<IOptions<TenantCatalogOptions>>().Value.DetailedResolutionErrors,
            hasHeaderSource
                ?
                [
                    .. services
                        .GetRequiredService<IOptions<HeaderTenantIdentifierSourceOptions>>()
                        .Value.HeaderNames.Distinct(StringComparer.OrdinalIgnoreCase),
                ]
                : [],
            hasHostSource
                ? [.. services.GetRequiredService<IOptions<HostTenantIdentifierSourceOptions>>().Value.Templates]
                : [],
            sources.Any(source => source is not HeaderTenantIdentifierSource)
        );
    }

    /// <summary>Whether the middleware resolves a tenant for the endpoint: it does unless the endpoint opts out.</summary>
    public static bool Resolves(ApiDescription description)
    {
        return !description.ActionDescriptor.EndpointMetadata.OfType<SkipTenantResolutionAttribute>().Any();
    }
}
