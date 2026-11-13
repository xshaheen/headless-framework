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
    TusContentCheck contentCheck,
    TusCompletionRunner completion,
    ITusFileLockProvider? fileLockProvider = null
)
{
    private readonly TusExtensions _allowedExtensions = _ExtensionsOf(store);

    public async Task<DefaultTusConfiguration> CreateAsync(HttpContext context, TusEndpointRegistration endpoint)
    {
        var endpointOptions = options.Get(endpoint.Name);

        _SetRequestBodyLimit(context, endpointOptions.MaxSize);

        var caller = await _GetCallerAsync(context, endpoint).ConfigureAwait(false);

        var stampedStore = new TusStampedStore(store, caller);

        // A write to, or the completion of, the upload this request created is part of that creating request,
        // not a request of its own.
        bool createdByThisRequest(string fileId) =>
            string.Equals(fileId, stampedStore.CreatedFileId, StringComparison.Ordinal);

        var events = new Events
        {
            OnAuthorizeAsync = authorize => _AuthorizeAsync(authorize, endpoint, caller),
            OnBeforeCreateAsync = create =>
                TusCreateValidation.ValidateAsync(create, endpoint, endpointOptions, _Owner(caller)),
            OnBeforeWriteAsync = write =>
                contentCheck.CheckAsync(write, endpointOptions, caller?.TenantId, createdByThisRequest(write.FileId)),
        };

        // Without a handler nothing is ever handed off, so a client may delete any of its uploads.
        if (endpoint.CompletedHandler is { } handlerType)
        {
            events.OnBeforeDeleteAsync = completion.AuthorizeDeleteAsync;
            events.OnFileCompleteAsync = complete =>
                completion.CompleteAsync(complete, handlerType, _Owner(caller), createdByThisRequest(complete.FileId));
        }

        var configuration = new DefaultTusConfiguration
        {
            Store = stampedStore,
            MaxAllowedUploadSizeInBytesLong = endpointOptions.MaxSize,
            Expiration = new SlidingExpiration(endpointOptions.Expiration),
            AllowedExtensions = _allowedExtensions,
            Events = events,
        };

        // Left unset, tusdotnet locks a file within this process only.
        if (fileLockProvider is not null)
        {
            configuration.FileLockProvider = fileLockProvider;
        }

        return configuration;
    }

    /// <summary>
    /// Decides whether the request may go on and, for a status request the upload's own caller sent, first runs
    /// a completion handler the upload still owes.
    /// </summary>
    private async Task _AuthorizeAsync(
        AuthorizeContext context,
        TusEndpointRegistration endpoint,
        TusUploadStamp? caller
    )
    {
        await authorizer.AuthorizeAsync(context, caller).ConfigureAwait(false);

        if (
            !context.HasFailed
            && caller is not null
            && context.Intent == IntentType.GetFileInfo
            && endpoint.CompletedHandler is { } handlerType
        )
        {
            await completion.RunOwedHandlerAsync(context, handlerType, caller).ConfigureAwait(false);
        }
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

    /// <summary>The stamp of a request tusdotnet lets create an upload.</summary>
    private static TusUploadStamp _Owner(TusUploadStamp? caller)
    {
        // Authorization refuses a creating request that has no owner before tusdotnet raises its create events.
        return caller
            ?? throw new InvalidOperationException("An upload cannot be created by a request that has no owner.");
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
