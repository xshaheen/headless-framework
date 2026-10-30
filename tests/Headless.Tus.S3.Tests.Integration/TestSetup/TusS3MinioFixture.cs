// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Headless.Tus;
using Microsoft.Extensions.Logging;

namespace Tests.TestSetup;

/// <summary>MinIO, reached as a self-hosted S3-compatible server.</summary>
[UsedImplicitly]
[CollectionDefinition]
public sealed class TusS3MinioFixture
    : HeadlessMinioFixture,
        ICollectionFixture<TusS3MinioFixture>,
        ITusConformanceBackend<TusS3Store>,
        IS3TestBackend
{
    private readonly TusS3StoreFactory _stores = new();

    public S3TestClient CreateClient()
    {
        return new S3TestClient(
            Container.GetConnectionString(),
            Container.GetAccessKey(),
            Container.GetSecretKey(),
            "us-east-1",
            TusS3Provider.S3Compatible
        );
    }

    public Task<TusS3Store> CreateStoreAsync(ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        return _stores.CreateAsync(CreateClient(), loggerFactory, cancellationToken);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _stores.Dispose();
        await base.DisposeAsyncCore();
    }
}
