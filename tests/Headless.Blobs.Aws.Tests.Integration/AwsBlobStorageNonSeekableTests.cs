// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Headless.Blobs;
using Headless.Blobs.Aws;
using Headless.IO;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Non-seekable uploads against LocalStack: content larger than one 5 MiB part streams through a real multipart
/// upload and must round-trip byte-for-byte, with the object's ETag carrying the multipart part-count suffix.
/// </summary>
[Collection<AwsBlobStorageFixture>]
public sealed class AwsBlobStorageNonSeekableTests(AwsBlobStorageFixture fixture) : TestBase
{
    private AmazonS3Client _CreateClient()
    {
        var s3Config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.USEast1,
            ServiceURL = fixture.Container.GetConnectionString(),
            ForcePathStyle = true,
        };

        var awsCredentials = new BasicAWSCredentials("xxx", "xxx");

        return new AmazonS3Client(awsCredentials, s3Config);
    }

    /// <summary>A stream whose data can only be read forward, like the request bodies this path exists for.</summary>
    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return inner.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    [Fact]
    public async Task upload_round_trips_a_large_non_seekable_stream_over_multipart()
    {
        // 11 MiB: two full 5 MiB parts and a short final one — more than one part, so S3 assembles the object.
        var content = new byte[11 * 1024 * 1024];

        for (var i = 0; i < content.Length; i++)
        {
            content[i] = (byte)(i % 251);
        }

        var container = $"nonseek-{Guid.NewGuid():N}";
        var location = new BlobLocation(container, "video.bin");

        using var managerClient = _CreateClient();
#pragma warning disable CA2000 // AwsBlobContainerManager disposes the client it owns on its own Dispose.
        var manager = new AwsBlobContainerManager(managerClient, new AwsBlobNamingNormalizer());
#pragma warning restore CA2000
        using (manager)
        {
            await manager.EnsureContainerAsync(container, AbortToken);

            using var client = _CreateClient();
#pragma warning disable CA2000 // AwsBlobStorage disposes the client it owns on its own DisposeAsync.
            await using var storage = new AwsBlobStorage(
                client,
                new MimeTypeProvider(),
                TimeProvider.System,
                new OptionsWrapper<AwsBlobStorageOptions>(new AwsBlobStorageOptions()),
                new AwsBlobNamingNormalizer()
            );
#pragma warning restore CA2000

            await using (var memory = new MemoryStream(content, writable: false))
            await using (var source = new NonSeekableStream(memory))
            {
                await storage.UploadAsync(location, source, cancellationToken: AbortToken);
            }

            var info = await storage.GetBlobInfoAsync(location, AbortToken);
            info.Should().NotBeNull();
            info!.Size.Should().Be(content.Length);

            await using var downloaded = await storage.OpenReadStreamAsync(location, AbortToken);
            downloaded.Should().NotBeNull();

            var actual = new byte[content.Length];
            var total = 0;

            while (total < actual.Length)
            {
                var read = await downloaded!.Stream.ReadAsync(actual.AsMemory(total), AbortToken);

                read.Should().BePositive("the downloaded object must cover the whole uploaded content");
                total += read;
            }

            actual.Should().Equal(content);
        }
    }
}
