// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.Xunit;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Shared MinIO (S3-compatible object storage) container fixture pinned to <see cref="TestImages.Minio"/>.
/// Use <see cref="ConnectionString"/>, <see cref="AccessKey"/>, and <see cref="SecretKey"/> to build a client.
/// </summary>
/// <remarks>
/// The fixture builds the container on Testcontainers' generic <see cref="ContainerBuilder"/>: Testcontainers removed
/// its MinIO module in 4.16, and the last module release fails at runtime against a newer Testcontainers core.
/// </remarks>
[PublicAPI]
public class HeadlessMinioFixture() : ContainerFixture<ContainerBuilder, IContainer>(TestContextMessageSink.Instance)
{
    /// <summary>The container port MinIO serves the S3 API on.</summary>
    public const ushort MinioPort = 9000;

    /// <summary>The root user the server starts with, used as the S3 access key: MinIO's own default.</summary>
    public const string AccessKey = "minioadmin";

    /// <summary>The root password the server starts with, used as the S3 secret key.</summary>
    public const string SecretKey = "minioadmin";

    /// <summary>Gets the S3 endpoint, an <c>http://</c> URL on the host port mapped to <see cref="MinioPort"/>.</summary>
    public string ConnectionString =>
        new UriBuilder(Uri.UriSchemeHttp, Container.Hostname, Container.GetMappedPublicPort(MinioPort)).ToString();

    /// <summary>
    /// Gets the image the fixture runs, <see cref="TestImages.Minio"/> unless overridden. Override it to run another
    /// version or a variant of the same server.
    /// </summary>
    /// <remarks>The image's entrypoint must be the <c>minio</c> binary, which the fixture runs as <c>server /data</c>.</remarks>
    protected virtual string Image => TestImages.Minio;

    protected override ContainerBuilder Configure()
    {
        return new ContainerBuilder(Image)
            .WithPortBinding(MinioPort, assignRandomHostPort: true)
            .WithCommand("server", "/data")
            .WithEnvironment("MINIO_ROOT_USER", AccessKey)
            .WithEnvironment("MINIO_ROOT_PASSWORD", SecretKey)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(request => request.ForPath("/minio/health/ready").ForPort(MinioPort))
            );
    }

    /// <inheritdoc />
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await ContainerHostPort
            .EnsureReachableAsync(Container, MinioPort, cancellationToken: TestContext.Current.CancellationToken)
            .ConfigureAwait(false);
    }
}
