// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using tusdotnet.FileLocks;
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

        var configuration = new DefaultTusConfiguration
        {
            Store = stampedStore,
            MaxAllowedUploadSizeInBytesLong = endpointOptions.MaxSize,
            Expiration = new SlidingExpiration(endpointOptions.Expiration),
            AllowedExtensions = _allowedExtensions,
        };

        configuration.Events = new Events
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
            OnBeforeWriteAsync = async write =>
            {
                if (await _ExceedsMaxSizeAsync(write, endpointOptions.MaxSize).ConfigureAwait(false))
                {
                    return;
                }

                await contentCheck
                    .CheckAsync(
                        write,
                        endpointOptions,
                        configuration.ClientReadTimeout,
                        // A write to the upload this request created is the data the creating request carries.
                        createdByThisRequest: string.Equals(
                            write.FileId,
                            stampedStore.CreatedFileId,
                            StringComparison.Ordinal
                        )
                    )
                    .ConfigureAwait(false);
            },
        };

        // Left unset, tusdotnet locks a file within this process only.
        var lockProvider = fileLockProvider ?? InMemoryFileLockProvider.Instance;

        configuration.FileLockProvider = store is ITusRequestSnapshotStore snapshotStore
            ? new TusSnapshotRefreshingLockProvider(lockProvider, snapshotStore)
            : lockProvider;

        return configuration;
    }

    /// <summary>
    /// Refuses a write to an upload whose declared length the endpoint no longer allows. tusdotnet checks a length
    /// against the limit only when a request declares it, so an upload created under a higher limit could otherwise
    /// finish after the limit was lowered. It runs under the upload's file lock.
    /// </summary>
    private static async Task<bool> _ExceedsMaxSizeAsync(BeforeWriteContext context, long maxSize)
    {
        var length = await context
            .Store.GetUploadLengthAsync(context.FileId, context.CancellationToken)
            .ConfigureAwait(false);

        if (length is null || length <= maxSize)
        {
            return false;
        }

        context.FailRequest(
            HttpStatusCode.RequestEntityTooLarge,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The upload is {length} bytes long, which is more than the {maxSize} bytes the endpoint allows."
            )
        );

        return true;
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
