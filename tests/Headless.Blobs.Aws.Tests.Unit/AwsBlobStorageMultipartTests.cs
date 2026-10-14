// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Blobs;
using Headless.Blobs.Aws;
using Headless.IO;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Tests;

/// <summary>
/// Uploads of non-seekable content, which stream through pooled part buffers instead of buffering the whole
/// content: one PutObject when the content fits a single part, an initiated-completed multipart upload otherwise,
/// and an abort that leaves no incomplete upload behind on any failure or cancellation.
/// </summary>
public sealed class AwsBlobStorageMultipartTests : TestBase
{
    private const int _PartSize = 5 * 1024 * 1024;

    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();

    private AwsBlobStorage _CreateSut()
    {
        return new AwsBlobStorage(
            _s3,
            new MimeTypeProvider(),
            TimeProvider.System,
            new OptionsWrapper<AwsBlobStorageOptions>(new AwsBlobStorageOptions()),
            new AwsBlobNamingNormalizer()
        );
    }

    private static byte[] _Drain(Stream stream)
    {
        using var drained = new MemoryStream();
        stream.CopyTo(drained);

        return drained.ToArray();
    }

    private static MemoryStream _Content(int size)
    {
        // Deterministic non-zero bytes: zero-filled parts would also pass a reassembly check that lost a part.
        var data = new byte[size];

        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        return new MemoryStream(data, writable: false);
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

    private void _ArrangeHappyPath()
    {
        _s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });
        _s3.InitiateMultipartUploadAsync(Arg.Any<InitiateMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new InitiateMultipartUploadResponse { HttpStatusCode = HttpStatusCode.OK, UploadId = "upload-1" });
        _s3.UploadPartAsync(Arg.Any<UploadPartRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var request = ci.Arg<UploadPartRequest>();

                // Drain the part the way the real SDK transport would, so a reassembly assertion also proves
                // each part's stream carries exactly its declared part size.
                _Drain(request.InputStream!);

                return Task.FromResult(
                    new UploadPartResponse
                    {
                        HttpStatusCode = HttpStatusCode.OK,
                        ETag = $"etag-{request.PartNumber}",
                        PartNumber = request.PartNumber,
                    }
                );
            });
        _s3.CompleteMultipartUploadAsync(Arg.Any<CompleteMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CompleteMultipartUploadResponse { HttpStatusCode = HttpStatusCode.OK });
        _s3.AbortMultipartUploadAsync(Arg.Any<AbortMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AbortMultipartUploadResponse { HttpStatusCode = HttpStatusCode.OK });
    }

    [Fact]
    public async Task upload_puts_non_seekable_content_shorter_than_one_part_in_a_single_put_object()
    {
        _ArrangeHappyPath();
        var sut = _CreateSut();

        await using var content = _Content(3 * 1024 * 1024);
        await using var source = new NonSeekableStream(content);
        await sut.UploadAsync(new BlobLocation("bucket", "video.bin"), source, cancellationToken: AbortToken);

        await _s3.ReceivedWithAnyArgs(1).PutObjectAsync(default!, AbortToken);
        await _s3.DidNotReceiveWithAnyArgs().InitiateMultipartUploadAsync(default!, AbortToken);
        await _s3.DidNotReceiveWithAnyArgs().UploadPartAsync(default!, AbortToken);
        await _s3.DidNotReceiveWithAnyArgs().CompleteMultipartUploadAsync(default!, AbortToken);
        await _s3.DidNotReceiveWithAnyArgs().AbortMultipartUploadAsync(default!, AbortToken);
    }

    [Fact]
    public async Task upload_puts_non_seekable_empty_content_in_a_single_put_object()
    {
        PutObjectRequest? captured = null;
        _ArrangeHappyPath();
        _s3.PutObjectAsync(Arg.Do<PutObjectRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });

        var sut = _CreateSut();

        await using var content = _Content(0);
        await using var source = new NonSeekableStream(content);
        await sut.UploadAsync(new BlobLocation("bucket", "video.bin"), source, cancellationToken: AbortToken);

        captured.Should().NotBeNull();
        _Drain(captured!.InputStream!)
            .Should()
            .BeEmpty("an empty non-seekable upload is an empty object, like a seekable empty stream");
    }

    [Fact]
    public async Task upload_streams_non_seekable_content_over_ordered_parts()
    {
        _ArrangeHappyPath();
        var captured = new List<(int PartNumber, long PartSize, bool IsLastPart, byte[] Bytes)>();
        _s3.UploadPartAsync(
                Arg.Do<UploadPartRequest>(r =>
                {
                    captured.Add((r.PartNumber ?? 0, r.PartSize ?? 0, r.IsLastPart, _Drain(r.InputStream!)));
                }),
                Arg.Any<CancellationToken>()
            )
            .Returns(new UploadPartResponse { HttpStatusCode = HttpStatusCode.OK, ETag = "etag-1" });

        var sut = _CreateSut();

        // 11 MiB: two full 5 MiB parts and a short final one.
        await using var content = _Content(11 * 1024 * 1024);
        await using var source = new NonSeekableStream(content);
        var expected = content.ToArray();
        await sut.UploadAsync(new BlobLocation("bucket", "video.bin"), source, cancellationToken: AbortToken);

        await _s3.DidNotReceiveWithAnyArgs().PutObjectAsync(default!, AbortToken);
        await _s3.ReceivedWithAnyArgs(1).InitiateMultipartUploadAsync(default!, AbortToken);
        await _s3.ReceivedWithAnyArgs(1).CompleteMultipartUploadAsync(default!, AbortToken);

        captured.Should().HaveCount(3);
        captured.Select(p => p.PartNumber).Should().ContainInOrder(1, 2, 3);
        captured.Select(p => p.PartSize).Should().ContainInOrder((long)_PartSize, (long)_PartSize, 1 * 1024 * 1024L);
        captured.Select(p => p.IsLastPart).Should().ContainInOrder(false, false, true);

        // The parts reassemble to the uploaded content in order: each full part carries its own slice, proving the
        // look-ahead read never overwrites a part still waiting to be uploaded.
        captured.SelectMany(p => p.Bytes).Should().Equal(expected);
    }

    [Fact]
    public async Task upload_marks_the_final_full_part_last_when_content_is_an_exact_part_multiple()
    {
        _ArrangeHappyPath();
        var sut = _CreateSut();

        // Exactly two part sizes: S3 has no zero-length trailing part to upload.
        await using var content = _Content(2 * _PartSize);
        await using var source = new NonSeekableStream(content);
        await sut.UploadAsync(new BlobLocation("bucket", "video.bin"), source, cancellationToken: AbortToken);

        await _s3.ReceivedWithAnyArgs(2).UploadPartAsync(default!, AbortToken);
        await _s3.Received()
            .UploadPartAsync(
                Arg.Is<UploadPartRequest>(r => r.PartNumber == 2 && r.IsLastPart && r.PartSize == _PartSize),
                Arg.Any<CancellationToken>()
            );
        await _s3.Received()
            .UploadPartAsync(
                Arg.Is<UploadPartRequest>(r => r.PartNumber == 1 && !r.IsLastPart && r.PartSize == _PartSize),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task upload_aborts_the_multipart_upload_when_a_part_fails()
    {
        _ArrangeHappyPath();
        _s3.UploadPartAsync(Arg.Any<UploadPartRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("part failed"));

        var sut = _CreateSut();

        await using var content = _Content(11 * 1024 * 1024);
        await using var source = new NonSeekableStream(content);
        var act = async () =>
        {
            await sut.UploadAsync(new BlobLocation("bucket", "video.bin"), source, cancellationToken: AbortToken);
        };

        await act.Should().ThrowAsync<AmazonS3Exception>();

        await _s3.Received(1)
            .AbortMultipartUploadAsync(
                Arg.Is<AbortMultipartUploadRequest>(r => r.UploadId == "upload-1"),
                Arg.Any<CancellationToken>()
            );
        await _s3.DidNotReceiveWithAnyArgs().CompleteMultipartUploadAsync(default!, AbortToken);
    }

    [Fact]
    public async Task upload_aborts_the_multipart_upload_when_completing_fails()
    {
        _ArrangeHappyPath();
        _s3.CompleteMultipartUploadAsync(Arg.Any<CompleteMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("complete failed"));

        var sut = _CreateSut();

        await using var content = _Content(11 * 1024 * 1024);
        await using var source = new NonSeekableStream(content);
        var act = async () =>
        {
            await sut.UploadAsync(new BlobLocation("bucket", "video.bin"), source, cancellationToken: AbortToken);
        };

        await act.Should().ThrowAsync<AmazonS3Exception>();

        await _s3.Received(1)
            .AbortMultipartUploadAsync(
                Arg.Is<AbortMultipartUploadRequest>(r => r.UploadId == "upload-1"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task upload_rethrows_the_original_failure_when_the_abort_itself_fails()
    {
        _ArrangeHappyPath();
        _s3.UploadPartAsync(Arg.Any<UploadPartRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("part failed"));
        _s3.AbortMultipartUploadAsync(Arg.Any<AbortMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("abort failed"));

        var sut = _CreateSut();

        await using var content = _Content(11 * 1024 * 1024);
        await using var source = new NonSeekableStream(content);
        var act = async () =>
        {
            await sut.UploadAsync(new BlobLocation("bucket", "video.bin"), source, cancellationToken: AbortToken);
        };

        (await act.Should().ThrowAsync<AmazonS3Exception>()).WithMessage("part failed");
    }

    [Fact]
    public async Task upload_aborts_the_multipart_upload_when_a_part_is_cancelled()
    {
        _ArrangeHappyPath();
        using var cancelled = new CancellationTokenSource();
        _s3.UploadPartAsync(Arg.Any<UploadPartRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
                Task.FromException<UploadPartResponse>(new OperationCanceledException(ci.Arg<CancellationToken>()))
            );

        var sut = _CreateSut();

        await using var content = _Content(6 * 1024 * 1024);
        await using var source = new NonSeekableStream(content);
        var act = async () =>
        {
            await sut.UploadAsync(new BlobLocation("bucket", "video.bin"), source, cancellationToken: cancelled.Token);
        };

        await act.Should().ThrowAsync<OperationCanceledException>();

        // The abort observes neither the cancelled token (it would be refused) nor fails the upload a second time.
        await _s3.Received(1)
            .AbortMultipartUploadAsync(
                Arg.Is<AbortMultipartUploadRequest>(r => r.UploadId == "upload-1"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task upload_leaves_no_multipart_state_when_the_first_part_already_fails()
    {
        _ArrangeHappyPath();
        _s3.InitiateMultipartUploadAsync(Arg.Any<InitiateMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("initiate failed"));

        var sut = _CreateSut();

        await using var content = _Content(11 * 1024 * 1024);
        await using var source = new NonSeekableStream(content);
        var act = async () =>
        {
            await sut.UploadAsync(new BlobLocation("bucket", "video.bin"), source, cancellationToken: AbortToken);
        };

        await act.Should().ThrowAsync<AmazonS3Exception>();

        await _s3.DidNotReceiveWithAnyArgs().AbortMultipartUploadAsync(default!, AbortToken);
        await _s3.DidNotReceiveWithAnyArgs().CompleteMultipartUploadAsync(default!, AbortToken);
    }
}
