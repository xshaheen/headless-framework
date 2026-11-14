// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
    TusContentCheck contentCheck,
    ITusFileLockProvider? fileLockProvider = null
)
{
    private readonly TusExtensions _allowedExtensions = _ExtensionsOf(store);

    public async Task<DefaultTusConfiguration> CreateAsync(HttpContext context, TusEndpointRegistration endpoint)
    {
        var endpointOptions = options.Get(endpoint.Name);

        _SetRequestBodyLimit(context, endpointOptions.MaxSize);

        var caller = await TusUploadBinding.ResolveCallerAsync(context, endpoint).ConfigureAwait(false);

        var stampedStore = new TusStampedStore(store, caller);

        // A write to the upload this request created is part of that creating request, not a request of its own.
        bool createdByThisRequest(string fileId) =>
            string.Equals(fileId, stampedStore.CreatedFileId, StringComparison.Ordinal);

        var configuration = new DefaultTusConfiguration
        {
            Store = stampedStore,
            MaxAllowedUploadSizeInBytesLong = endpointOptions.MaxSize,
            Expiration = new SlidingExpiration(endpointOptions.Expiration),
            AllowedExtensions = _allowedExtensions,
            Events = new Events
            {
                OnAuthorizeAsync = authorize => authorizer.AuthorizeAsync(authorize, caller),
                OnBeforeDeleteAsync = authorizer.AuthorizeDeleteAsync,
                OnBeforeCreateAsync = create =>
                    // Authorization has already refused a creating request that has no owner.
                    TusCreateValidation.ValidateAsync(
                        create,
                        endpoint,
                        endpointOptions,
                        TusUploadStamp.RequireOwner(caller)
                    ),
                OnBeforeWriteAsync = write =>
                    contentCheck.CheckAsync(
                        write,
                        endpointOptions,
                        caller?.TenantId,
                        createdByThisRequest(write.FileId)
                    ),
            },
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
