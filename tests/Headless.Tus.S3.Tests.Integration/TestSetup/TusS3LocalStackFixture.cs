// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Headless.Tus;
using Microsoft.Extensions.Logging;
using Testcontainers.LocalStack;

namespace Tests.TestSetup;

/// <summary>LocalStack, standing in for Amazon S3.</summary>
[UsedImplicitly]
[CollectionDefinition]
public sealed class TusS3LocalStackFixture
    : HeadlessLocalStackFixture,
        ICollectionFixture<TusS3LocalStackFixture>,
        ITusConformanceBackend<TusS3Store>,
        IS3TestBackend
{
    private readonly TusS3StoreFactory _stores = new();

    public S3TestClient CreateClient()
    {
        return new S3TestClient(Container.GetConnectionString(), "test", "test", "us-east-1", TusS3Provider.Aws);
    }

    public Task<TusS3Store> CreateStoreAsync(ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        return _stores.CreateAsync(CreateClient(), loggerFactory, cancellationToken);
    }

    // A random host port (the container builder's default): a fixed binding collides when two worktrees run the suite.
    protected override LocalStackBuilder Configure()
    {
        return base.Configure().WithEnvironment("SERVICES", "s3");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _stores.Dispose();
        await base.DisposeAsyncCore();
    }
}
