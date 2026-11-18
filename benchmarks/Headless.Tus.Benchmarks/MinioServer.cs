// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Headless.Tus.Benchmarks;

/// <summary>One MinIO container both stores upload to, each into its own bucket.</summary>
internal sealed class MinioServer : IAsyncDisposable
{
    // The image the MinIO integration fixtures pin: Docker Hub removed minio/minio, and the digest keeps a fresh pull
    // from changing server behavior between runs.
    private const string _Image =
        "chainguard/minio:latest@sha256:4cf4831a2bbcf13ddca09c1cbcc9faff716dd3c4247e0babc32864b8ee8e0034";

    private const ushort _Port = 9000;
    private const string _AccessKey = "minio-benchmark";
    private const string _SecretKey = "minio-benchmark-secret";

    private readonly IContainer _container;

    private MinioServer(IContainer container)
    {
        _container = container;
    }

    public static async Task<MinioServer> StartAsync()
    {
        // A plain container: Testcontainers removed its MinIO module, and the last release fails against a newer core.
        var container = new ContainerBuilder(_Image)
            .WithPortBinding(_Port, assignRandomHostPort: true)
            .WithCommand("server", "/data")
            .WithEnvironment("MINIO_ROOT_USER", _AccessKey)
            .WithEnvironment("MINIO_ROOT_PASSWORD", _SecretKey)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(request => request.ForPath("/minio/health/ready").ForPort(_Port))
            )
            .Build();
        await container.StartAsync();

        return new MinioServer(container);
    }

    /// <summary>
    /// Creates an S3 client configured the way the docs tell an application to reach a non-AWS endpoint: path style,
    /// an explicit authentication region, and checksums only where an operation requires them.
    /// </summary>
    public AmazonS3Client CreateClient(HttpClientFactory? httpClientFactory = null)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = new UriBuilder(
                Uri.UriSchemeHttp,
                _container.Hostname,
                _container.GetMappedPublicPort(_Port)
            ).ToString(),
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        if (httpClientFactory is not null)
        {
            config.HttpClientFactory = httpClientFactory;
        }

        return new AmazonS3Client(new BasicAWSCredentials(_AccessKey, _SecretKey), config);
    }

    public async Task CreateBucketAsync(string bucket)
    {
        using var client = CreateClient();
        await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket });
    }

    public ValueTask DisposeAsync()
    {
        return _container.DisposeAsync();
    }
}
