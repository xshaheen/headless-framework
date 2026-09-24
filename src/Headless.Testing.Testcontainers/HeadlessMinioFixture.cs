// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Testcontainers.Minio;
using Testcontainers.Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Shared MinIO (S3-compatible object storage) container fixture pinned to <see cref="TestImages.Minio"/>.
/// Use <c>Container.GetConnectionString()</c>, <c>Container.GetAccessKey()</c>, and
/// <c>Container.GetSecretKey()</c> to build a client.
/// </summary>
[PublicAPI]
public class HeadlessMinioFixture() : ContainerFixture<MinioBuilder, MinioContainer>(TestContextMessageSink.Instance)
{
    protected override MinioBuilder Configure()
    {
        return base.Configure()
            .WithImage(TestImages.Minio)
            .WithReuse(true)
            .WithLabel(ReuseLabel.Key, ReuseLabel.For(this));
    }
}
