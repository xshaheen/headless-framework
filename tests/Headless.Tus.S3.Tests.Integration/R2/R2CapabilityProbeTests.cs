// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Tus;
using Tests.Probes;
using Tests.TestSetup;

namespace Tests.R2;

/// <summary>
/// The capability probes against a real Cloudflare R2 account, plus R2's rule that every part but the last has the
/// same size, which no emulator enforces. There is no R2 emulator, so every scenario that touches storage skips when
/// the R2_* environment variables are absent (local runs, forks). The token must allow bucket creation.
/// </summary>
public sealed class R2CapabilityProbeTests : S3CapabilityProbeTestsBase
{
    protected override S3TestClient CreateClient()
    {
        var accountId = Environment.GetEnvironmentVariable("R2_ACCOUNT_ID");
        var accessKey = Environment.GetEnvironmentVariable("R2_ACCESS_KEY_ID");
        var secretKey = Environment.GetEnvironmentVariable("R2_SECRET_ACCESS_KEY");

        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(accountId)
                || string.IsNullOrWhiteSpace(accessKey)
                || string.IsNullOrWhiteSpace(secretKey),
            "R2 credentials are not configured (R2_ACCOUNT_ID, R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY)."
        );

        return new S3TestClient(
            $"https://{accountId}.r2.cloudflarestorage.com",
            accessKey!,
            secretKey!,
            "auto",
            TusS3Provider.R2
        );
    }

    [Fact]
    public override Task should_create_only_when_key_is_absent_with_if_none_match() =>
        base.should_create_only_when_key_is_absent_with_if_none_match();

    [Fact]
    public override Task should_replace_only_the_etag_a_writer_read_with_if_match() =>
        base.should_replace_only_the_etag_a_writer_read_with_if_match();

    [Fact]
    public override Task should_report_no_such_key_when_if_match_targets_a_missing_key() =>
        base.should_report_no_such_key_when_if_match_targets_a_missing_key();

    [Fact]
    public override Task should_complete_a_multipart_upload_from_a_subset_of_its_parts() =>
        base.should_complete_a_multipart_upload_from_a_subset_of_its_parts();

    [Fact]
    public override Task should_copy_a_byte_range_of_an_object_into_a_part() =>
        base.should_copy_a_byte_range_of_an_object_into_a_part();

    [Fact]
    public async Task should_reject_a_multipart_upload_whose_non_final_parts_differ_in_size()
    {
        // given - two non-final parts of different sizes, both above the minimum
        await StartAsync();
        var key = $"probe/{Guid.NewGuid():N}";
        var uploadId = await InitiateAsync(key);

        // when - R2 may refuse the odd part on upload or the whole set on completion; either is a rejection
        var act = async () =>
        {
            var first = await UploadPartAsync(key, uploadId, 1, RandomBytes(MinPartSize, seed: 1));
            var second = await UploadPartAsync(key, uploadId, 2, RandomBytes(MinPartSize + (1024 * 1024), seed: 2));
            var last = await UploadPartAsync(key, uploadId, 3, RandomBytes(1024, seed: 3));

            await S3.CompleteMultipartUploadAsync(
                new CompleteMultipartUploadRequest
                {
                    BucketName = Bucket,
                    Key = key,
                    UploadId = uploadId,
                    PartETags = [new PartETag(1, first), new PartETag(2, second), new PartETag(3, last)],
                },
                AbortToken
            );
        };

        // then
        await act.Should().ThrowAsync<AmazonS3Exception>();
    }

    // Needs no credentials or network: the request stops at a local handler. It keeps the module from reporting zero
    // tests without R2 credentials, and proves the R2 preset reaches the wire as an unsigned payload over https.
    [Fact]
    public async Task should_send_part_uploads_unsigned_with_the_r2_preset()
    {
        // given
        using var capture = new CapturingHandler();
        using var client = new AmazonS3Client(
            new BasicAWSCredentials("ak", "sk"),
            new AmazonS3Config
            {
                ServiceURL = "https://testacc.r2.cloudflarestorage.com",
                ForcePathStyle = true,
                AuthenticationRegion = "auto",
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                HttpClientFactory = new CapturingHttpClientFactory(capture),
            }
        );

        var request = new UploadPartRequest
        {
            BucketName = "bucket",
            Key = "key",
            UploadId = "upload",
            PartNumber = 1,
            InputStream = new MemoryStream(RandomBytes(1024, seed: 1)),
            PartSize = 1024,
        };
        TusS3RequestPresets.Apply(request, TusS3Provider.R2);

        // when
        await client.UploadPartAsync(request, AbortToken);

        // then
        capture.ContentSha256.Should().Be("UNSIGNED-PAYLOAD");
        capture.ContentEncoding.Should().NotContain("aws-chunked");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? ContentSha256 { get; private set; }

        public IReadOnlyList<string> ContentEncoding { get; private set; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            ContentSha256 = request.Headers.TryGetValues("x-amz-content-sha256", out var values)
                ? values.Single()
                : null;
            ContentEncoding = [.. request.Content?.Headers.ContentEncoding ?? []];

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"etag\"");

            return Task.FromResult(response);
        }
    }

    private sealed class CapturingHttpClientFactory(CapturingHandler handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig)
        {
            return new HttpClient(handler, disposeHandler: false);
        }

        public override bool UseSDKHttpClientCaching(IClientConfig clientConfig)
        {
            return false;
        }

        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig)
        {
            return true;
        }
    }
}
