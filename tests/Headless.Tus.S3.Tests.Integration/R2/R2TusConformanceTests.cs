// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3.Model;
using Headless.Tus;
using Microsoft.Extensions.Logging;
using Tests.TestSetup;

namespace Tests.R2;

// The shared tus conformance scenarios against a real Cloudflare R2 account, the same set the MinIO and LocalStack
// classes run. There is no R2 emulator, so every scenario skips when the R2_* environment variables are absent (local
// runs, forks); R2CapabilityProbeTests holds the credential-free fact that keeps the module from reporting zero tests.

/// <summary>
/// Cloudflare R2, reached with the configuration the docs give an application. The run gets its own bucket, created on
/// the first scenario and dropped with everything in it when the collection finishes, so a real account keeps no
/// conformance state and two runs never share uploads. The token must allow bucket creation.
/// </summary>
[UsedImplicitly]
[CollectionDefinition]
public sealed class TusS3R2Fixture
    : ICollectionFixture<TusS3R2Fixture>,
        ITusConformanceBackend<TusS3Store>,
        IAsyncDisposable
{
    private readonly string _bucket = $"tus-conformance-{Guid.NewGuid():N}";
    private readonly TusS3StoreFactory _stores;
    private volatile bool _bucketTouched;

    public TusS3R2Fixture()
    {
        _stores = new TusS3StoreFactory(_bucket);
    }

    public Task<TusS3Store> CreateStoreAsync(ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var client = _CreateClient();
        _bucketTouched = true;

        return _stores.CreateAsync(client, loggerFactory, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_bucketTouched)
            {
                using var client = _CreateClient();
                await _DeleteBucketAsync(client);
            }
        }
        finally
        {
            _stores.Dispose();
        }
    }

    private static S3TestClient _CreateClient()
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

    // Aborts the multipart uploads the scenarios left open, empties the bucket page by page, and drops it. Teardown
    // runs after the scenarios' own tokens are gone, so it does not take one.
    private async Task _DeleteBucketAsync(S3TestClient client)
    {
        var s3 = client.S3;
        var uploads = new ListMultipartUploadsRequest { BucketName = _bucket };

        while (true)
        {
            var page = await s3.ListMultipartUploadsAsync(uploads, CancellationToken.None);

            foreach (var upload in page.MultipartUploads ?? [])
            {
                await s3.AbortMultipartUploadAsync(_bucket, upload.Key, upload.UploadId, CancellationToken.None);
            }

            if (page.IsTruncated != true)
            {
                break;
            }

            uploads.KeyMarker = page.NextKeyMarker;
            uploads.UploadIdMarker = page.NextUploadIdMarker;
        }

        var objects = new ListObjectsV2Request { BucketName = _bucket };

        while (true)
        {
            var page = await s3.ListObjectsV2Async(objects, CancellationToken.None);
            var keys = (page.S3Objects ?? []).ConvertAll(o => new KeyVersion { Key = o.Key });

            if (keys.Count > 0)
            {
                await s3.DeleteObjectsAsync(
                    new DeleteObjectsRequest { BucketName = _bucket, Objects = keys },
                    CancellationToken.None
                );
            }

            if (page.IsTruncated != true)
            {
                break;
            }

            objects.ContinuationToken = page.NextContinuationToken;
        }

        await s3.DeleteBucketAsync(_bucket, CancellationToken.None);
    }
}

[Collection<TusS3R2Fixture>]
public sealed class R2TusS3StoreConformanceTests(TusS3R2Fixture fixture)
    : TusStoreConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task should_get_upload_metadata() => base.should_get_upload_metadata();

    [Fact]
    public override Task should_get_file() => base.should_get_file();

    [Fact]
    public override Task should_round_trip_metadata_with_non_ascii_values_and_mixed_case_keys() =>
        base.should_round_trip_metadata_with_non_ascii_values_and_mixed_case_keys();

    [Fact]
    public override Task should_upload_data_before_upload_length_is_known() =>
        base.should_upload_data_before_upload_length_is_known();

    [Fact]
    public override Task should_check_file_existence() => base.should_check_file_existence();

    [Fact]
    public override Task should_get_upload_length() => base.should_get_upload_length();

    [Fact]
    public override Task should_get_expiration() => base.should_get_expiration();

    [Fact]
    public override Task should_append_data_when_file_exists() => base.should_append_data_when_file_exists();

    [Fact]
    public override Task should_throw_when_append_data_to_nonexistent_file() =>
        base.should_throw_when_append_data_to_nonexistent_file();

    [Fact]
    public override Task should_append_data_using_pipe_when_file_exists() =>
        base.should_append_data_using_pipe_when_file_exists();

    [Fact]
    public override Task should_throw_when_append_data_using_pipe_to_nonexistent_file() =>
        base.should_throw_when_append_data_using_pipe_to_nonexistent_file();

    [Fact]
    public override Task should_persist_received_bytes_when_client_disconnects_mid_stream() =>
        base.should_persist_received_bytes_when_client_disconnects_mid_stream();

    [Fact]
    public override Task should_persist_received_bytes_when_pipe_read_is_cancelled() =>
        base.should_persist_received_bytes_when_pipe_read_is_cancelled();
}

[Collection<TusS3R2Fixture>]
public sealed class R2TusS3ConcatenationConformanceTests(TusS3R2Fixture fixture)
    : TusConcatenationConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task should_get_partial_file_info() => base.should_get_partial_file_info();

    [Fact]
    public override Task should_return_null_for_regular_file() => base.should_return_null_for_regular_file();

    [Fact]
    public override Task should_return_null_for_nonexistent_file() => base.should_return_null_for_nonexistent_file();
}

[Collection<TusS3R2Fixture>]
public sealed class R2TusS3ProtocolConformanceTests(TusS3R2Fixture fixture)
    : TusProtocolConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task head_echoes_metadata_verbatim_and_reports_length_and_offset() =>
        base.head_echoes_metadata_verbatim_and_reports_length_and_offset();

    [Fact]
    public override Task defer_length_upload_reports_defer_header_until_length_is_declared() =>
        base.defer_length_upload_reports_defer_header_until_length_is_declared();

    [Fact]
    public override Task patch_advances_offset_and_reports_expiration() =>
        base.patch_advances_offset_and_reports_expiration();

    [Fact]
    public override Task patch_with_wrong_offset_conflicts_without_modifying_the_upload() =>
        base.patch_with_wrong_offset_conflicts_without_modifying_the_upload();
}

[Collection<TusS3R2Fixture>]
public sealed class R2TusS3UploadFlowConformanceTests(TusS3R2Fixture fixture)
    : TusUploadFlowConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task should_complete_small_file_upload() => base.should_complete_small_file_upload();

    [Fact]
    public override Task should_resume_interrupted_upload() => base.should_resume_interrupted_upload();

    [Fact]
    public override Task should_handle_concurrent_uploads() => base.should_handle_concurrent_uploads();

    [Fact]
    public override Task should_handle_large_file_chunking() => base.should_handle_large_file_chunking();
}

[Collection<TusS3R2Fixture>]
public sealed class R2TusS3ExpirationConformanceTests(TusS3R2Fixture fixture)
    : TusExpirationConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task should_cleanup_expired_uploads() => base.should_cleanup_expired_uploads();

    [Fact]
    public override Task should_not_delete_expired_completed_uploads() =>
        base.should_not_delete_expired_completed_uploads();

    [Fact]
    public override Task should_cleanup_expired_defer_length_uploads() =>
        base.should_cleanup_expired_defer_length_uploads();

    [Fact]
    public override Task should_not_delete_active_uploads() => base.should_not_delete_active_uploads();

    [Fact]
    public override Task should_cleanup_expired_incomplete_uploads() =>
        base.should_cleanup_expired_incomplete_uploads();

    [Fact]
    public override Task should_report_no_expiration_for_completed_uploads() =>
        base.should_report_no_expiration_for_completed_uploads();

    [Fact]
    public override Task should_set_expiration_even_when_request_token_is_cancelled() =>
        base.should_set_expiration_even_when_request_token_is_cancelled();

    [Fact]
    public override Task should_list_only_expired_files() => base.should_list_only_expired_files();

    [Fact]
    public override Task should_return_partial_results_instead_of_throwing_when_cancelled() =>
        base.should_return_partial_results_instead_of_throwing_when_cancelled();
}

[Collection<TusS3R2Fixture>]
public sealed class R2TusS3ExpiredFilesConformanceTests(TusS3R2Fixture fixture)
    : TusStoreConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task should_get_expired_files() => base.should_get_expired_files();
}

[Collection<TusS3R2Fixture>]
public sealed class R2TusS3TerminationConformanceTests(TusS3R2Fixture fixture)
    : TusProtocolConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task delete_terminates_the_upload() => base.delete_terminates_the_upload();
}

[Collection<TusS3R2Fixture>]
public sealed class R2TusS3FinalUploadConformanceTests(TusS3R2Fixture fixture)
    : TusConcatenationConformanceTestsBase<TusS3Store>(fixture)
{
    [Fact]
    public override Task should_throw_when_create_final_file_with_empty_partials() =>
        base.should_throw_when_create_final_file_with_empty_partials();

    [Fact]
    public override Task should_throw_when_create_final_file_with_nonexistent_partial() =>
        base.should_throw_when_create_final_file_with_nonexistent_partial();

    [Fact]
    public override Task should_throw_when_create_final_file_with_regular_file_as_partial() =>
        base.should_throw_when_create_final_file_with_regular_file_as_partial();

    [Fact]
    public override Task should_throw_when_create_final_file_with_incomplete_partial() =>
        base.should_throw_when_create_final_file_with_incomplete_partial();

    [Fact]
    public override Task should_get_final_file_info() => base.should_get_final_file_info();

    [Fact]
    public override Task should_preserve_data_integrity_after_concatenation() =>
        base.should_preserve_data_integrity_after_concatenation();

    [Fact]
    public override Task should_create_final_file_from_single_partial() =>
        base.should_create_final_file_from_single_partial();

    [Fact]
    public override Task should_allow_same_partial_multiple_times_in_final_file() =>
        base.should_allow_same_partial_multiple_times_in_final_file();

    [Fact]
    public override Task should_keep_partials_after_concat_by_default() =>
        base.should_keep_partials_after_concat_by_default();

    [Fact]
    public override Task should_preserve_user_metadata_in_final_file() =>
        base.should_preserve_user_metadata_in_final_file();
}
