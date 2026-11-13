// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Models.Expiration;

namespace Headless.Tus.Internal;

/// <summary>
/// Builds the tusdotnet configuration for one request to a mapped upload endpoint. It is the only place that
/// turns an endpoint's declaration into what tusdotnet runs.
/// </summary>
internal sealed class TusConfigurationFactory(
    ITusStore store,
    IOptionsMonitor<TusUploadEndpointOptions> options,
    TusUploadAuthorizer authorizer,
    ITusFileLockProvider? fileLockProvider = null
)
{
    private readonly TusExtensions _allowedExtensions = _ExtensionsOf(store);

    public async Task<DefaultTusConfiguration> CreateAsync(HttpContext context, TusEndpointRegistration endpoint)
    {
        var endpointOptions = options.Get(endpoint.Name);

        _SetRequestBodyLimit(context, endpointOptions.MaxSize);

        var caller = await _GetCallerAsync(context, endpoint).ConfigureAwait(false);

        var configuration = new DefaultTusConfiguration
        {
            Store = new TusStampedStore(store, caller),
            MaxAllowedUploadSizeInBytesLong = endpointOptions.MaxSize,
            Expiration = new SlidingExpiration(endpointOptions.Expiration),
            AllowedExtensions = _allowedExtensions,
            Events = new Events { OnAuthorizeAsync = authorize => authorizer.AuthorizeAsync(authorize, caller) },
        };

        // Left unset, tusdotnet locks a file within this process only.
        if (fileLockProvider is not null)
        {
            configuration.FileLockProvider = fileLockProvider;
        }

        return configuration;
    }

    /// <summary>
    /// The tus extensions the endpoint offers: the ones the host's store supports, except concatenation. tusdotnet
    /// would read them off the store it is given, but that store is the per-request wrapper, which implements
    /// every capability, so they are read off the host's store here. Leaving concatenation out stops tusdotnet
    /// from accepting an <c>Upload-Concat</c> request, not only from advertising the extension.
    /// </summary>
    private static TusExtensions _ExtensionsOf(ITusStore store)
    {
        List<TusExtensions> extensions = [];

        if (store is ITusCreationStore)
        {
            extensions.Add(TusExtensions.Creation);
            extensions.Add(TusExtensions.CreationWithUpload);
        }

        if (store is ITusCreationDeferLengthStore)
        {
            extensions.Add(TusExtensions.CreationDeferLength);
        }

        if (store is ITusTerminationStore)
        {
            extensions.Add(TusExtensions.Termination);
        }

        if (store is ITusChecksumStore)
        {
            extensions.Add(TusExtensions.Checksum);
            extensions.Add(TusExtensions.ChecksumTrailer);
        }

        if (store is ITusExpirationStore)
        {
            extensions.Add(TusExtensions.Expiration);
        }

        return new TusExtensions([.. extensions]);
    }

    /// <summary>
    /// Returns the stamp an upload created by this request gets, which is also what an upload the request names
    /// must carry, or <see langword="null"/> when the request has no owner.
    /// </summary>
    private static async ValueTask<TusUploadStamp?> _GetCallerAsync(
        HttpContext context,
        TusEndpointRegistration endpoint
    )
    {
        // Resolved from the request's services: an application may register either one per request.
        var ownerKey = endpoint.OwnerKey is { } ownedBy
            ? await ownedBy(context).ConfigureAwait(false)
            : context.RequestServices.GetService<ICurrentUser>()?.UserId?.GetUnderlyingPrimitiveValue();

        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return null;
        }

        var tenantId = context.RequestServices.GetService<ICurrentTenant>()?.Id;

        return new TusUploadStamp(endpoint.Name, ownerKey, string.IsNullOrEmpty(tenantId) ? null : tenantId);
    }

    /// <summary>
    /// Makes the server accept a request body as large as the endpoint's uploads. tusdotnet leaves the server's
    /// limit alone, and a client may send a whole upload in one request, so the server default (30 MB on Kestrel)
    /// would refuse an upload the endpoint allows.
    /// </summary>
    private static void _SetRequestBodyLimit(HttpContext context, long maxSize)
    {
        // The feature is missing on a server that enforces no limit, and read-only once the body is being read.
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
        {
            bodySize.MaxRequestBodySize = maxSize;
        }
    }
}
