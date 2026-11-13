// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Expiration;

namespace Headless.Tus.Internal;

/// <summary>
/// Builds the tusdotnet configuration for one request to a mapped upload endpoint. It is the only place that
/// turns an endpoint's declaration into what tusdotnet runs.
/// </summary>
internal sealed class TusConfigurationFactory(
    ITusStore store,
    IOptionsMonitor<TusUploadEndpointOptions> options,
    ITusFileLockProvider? fileLockProvider = null
)
{
    // tusdotnet offers an allowed extension only when the store also supports it, so this is the store's
    // capabilities minus concatenation. Leaving concatenation out stops tusdotnet from accepting an Upload-Concat
    // request, not only from advertising the extension.
    private static readonly TusExtensions _AllowedExtensions = TusExtensions.All.Except(TusExtensions.Concatenation);

    public Task<DefaultTusConfiguration> CreateAsync(HttpContext context, TusEndpointRegistration endpoint)
    {
        var endpointOptions = options.Get(endpoint.Name);

        _SetRequestBodyLimit(context, endpointOptions.MaxSize);

        var configuration = new DefaultTusConfiguration
        {
            Store = store,
            MaxAllowedUploadSizeInBytesLong = endpointOptions.MaxSize,
            Expiration = new SlidingExpiration(endpointOptions.Expiration),
            AllowedExtensions = _AllowedExtensions,
        };

        // Left unset, tusdotnet locks a file within this process only.
        if (fileLockProvider is not null)
        {
            configuration.FileLockProvider = fileLockProvider;
        }

        return Task.FromResult(configuration);
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
