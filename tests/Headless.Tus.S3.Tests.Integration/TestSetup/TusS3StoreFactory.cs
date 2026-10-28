// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Tus;
using Headless.Tus.Internal;
using Microsoft.Extensions.Logging;

namespace Tests.TestSetup;

/// <summary>
/// Builds conformance stores over one shared, provisioned bucket and keeps their clients alive until the fixture is
/// disposed, because a store does not own the client it is given.
/// </summary>
internal sealed class TusS3StoreFactory : IDisposable
{
    /// <summary>The bucket every conformance scenario on one backend shares.</summary>
    public const string Bucket = "tus-conformance";

    /// <summary>The key prefix under <see cref="Bucket" /> the shared stores write to.</summary>
    public const string Prefix = "tusfiles/";

    private readonly ConcurrentBag<S3TestClient> _clients = [];

    public async Task<TusS3Store> CreateAsync(
        S3TestClient client,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        _clients.Add(client);

        var options = new TusS3StoreOptions
        {
            Bucket = Bucket,
            Prefix = Prefix,
            Provider = client.Provider,
        };

        // The store does not create its bucket; the deploy-time provisioner does, so every store is provisioned
        // before a scenario touches it.
        await new TusS3BucketProvisioner(client.S3, options, loggerFactory).ProvisionAsync(cancellationToken);

        return new TusS3Store(client.S3, options, loggerFactory: loggerFactory);
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }
    }
}
