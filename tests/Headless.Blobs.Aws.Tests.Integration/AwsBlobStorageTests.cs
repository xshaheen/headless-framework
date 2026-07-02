// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Headless.Abstractions;
using Headless.Blobs;
using Headless.Blobs.Aws;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection<AwsBlobStorageFixture>]
public sealed class AwsBlobStorageTests(AwsBlobStorageFixture fixture) : BlobStorageTestsBase
{
    // AWS S3 supports container (bucket) lifecycle, so the conformance suite resolves the dedicated
    // IBlobContainerManager capability (constructed directly, never cast from the storage instance).
    private AwsBlobContainerManager? _manager;

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

    protected override IBlobStorage GetStorage()
    {
#pragma warning disable CA2000 // Disposed by AwsBlobStorage on its own DisposeAsync.
        var amazonS3Client = _CreateClient();
#pragma warning restore CA2000

        var options = new OptionsWrapper<AwsBlobStorageOptions>(new AwsBlobStorageOptions());

        return new AwsBlobStorage(
            amazonS3Client,
            new MimeTypeProvider(),
            new Clock(TimeProvider.System),
            options,
            new AwsBlobNamingNormalizer()
        );
    }

    // Container management is a separately-resolved capability (never a cast from IBlobStorage). The fixture owns the
    // LocalStack endpoint, so the manager is constructed from a per-test S3 client + the AWS normalizer and cached.
    protected override IBlobContainerManager GetContainerManager()
    {
#pragma warning disable CA2000 // Owned + disposed by the AwsBlobContainerManager, released in DisposeAsyncCore.
        return _manager ??= new AwsBlobContainerManager(_CreateClient(), new AwsBlobNamingNormalizer());
#pragma warning restore CA2000
    }

    // Mixed-case container that the AWS normalizer lowercases onto ContainerName's backing bucket ("storage"), so the
    // normalization round-trip proves upload, bulk-delete, and info all route through the same resolve seam (H1/H2).
    protected override string NormalizationSensitiveContainer => "STORAGE";

    protected override async ValueTask DisposeAsyncCore()
    {
        _manager?.Dispose();
        _manager = null;

        await base.DisposeAsyncCore();
    }

    #region Provider-specific scenarios

    [Fact]
    public async Task upload_throws_when_bucket_missing()
    {
        // The data plane never auto-creates a missing bucket; a missing
        // top-level container surfaces as an S3 error rather than silently creating the bucket.
        await using var storage = GetStorage();

        var location = new BlobLocation($"missing-{Guid.NewGuid():N}", "file.txt");
        using var stream = new MemoryStream("hello"u8.ToArray());

        var act = async () => await storage.UploadAsync(location, stream, cancellationToken: AbortToken);

        await act.Should().ThrowAsync<AmazonS3Exception>();
    }

    [Fact]
    public async Task can_round_trip_via_presigned_download_url()
    {
        await using var storage = GetStorage();
        var presigned = (IPresignedUrlBlobStorage)storage;

        var container = $"presign-{Guid.NewGuid():N}";
        var location = new BlobLocation(container, "file.txt");
        var content = "presigned-content"u8.ToArray();

        // The bucket must exist before the upload; ensure it through the container-management capability.
        await GetContainerManager().EnsureContainerAsync(container, AbortToken);

        using (var stream = new MemoryStream(content))
        {
            await storage.UploadAsync(location, stream, cancellationToken: AbortToken);
        }

        var url = await presigned.GetPresignedDownloadUrlAsync(location, TimeSpan.FromMinutes(5), AbortToken);

        using var http = new HttpClient();
        var downloaded = await http.GetByteArrayAsync(url, AbortToken);

        downloaded.Should().Equal(content);
    }

    [Fact]
    public async Task can_round_trip_via_presigned_upload_url()
    {
        await using var storage = GetStorage();
        var presigned = (IPresignedUrlBlobStorage)storage;

        var container = $"presign-{Guid.NewGuid():N}";
        var location = new BlobLocation(container, "file.txt");
        var content = "presigned-upload"u8.ToArray();

        // The presigned PUT goes straight to S3 and does not create the bucket; ensure it first.
        await GetContainerManager().EnsureContainerAsync(container, AbortToken);

        var uploadUrl = await presigned.GetPresignedUploadUrlAsync(location, TimeSpan.FromMinutes(5), AbortToken);

        using (var http = new HttpClient())
        using (var body = new ByteArrayContent(content))
        {
            var response = await http.PutAsync(uploadUrl, body, AbortToken);
            response.EnsureSuccessStatusCode();
        }

        await using var readBack = await storage.OpenReadStreamAsync(location, AbortToken);
        readBack.Should().NotBeNull();

        using var buffer = new MemoryStream();
        await readBack!.Stream.CopyToAsync(buffer, AbortToken);
        buffer.ToArray().Should().Equal(content);
    }

    #endregion

    #region List / Round-trip

    [Fact]
    public override Task can_get_empty_file_list_on_missing_directory() =>
        base.can_get_empty_file_list_on_missing_directory();

    [Fact]
    public override Task can_get_file_list_for_single_folder() => base.can_get_file_list_for_single_folder();

    [Fact]
    public override Task can_get_file_list_for_single_file() => base.can_get_file_list_for_single_file();

    [Fact]
    public override Task can_get_file_info() => base.can_get_file_info();

    [Fact]
    public override Task can_get_non_existent_file_info() => base.can_get_non_existent_file_info();

    [Fact]
    public override Task can_manage_files() => base.can_manage_files();

    [Fact]
    public override Task can_move_files() => base.can_move_files();

    [Fact]
    public override Task can_round_trip_seekable_stream() => base.can_round_trip_seekable_stream();

    [Fact]
    public override Task will_reset_stream_position() => base.will_reset_stream_position();

    [Fact]
    public override Task can_save_over_existing_stored_content() => base.can_save_over_existing_stored_content();

    [Fact]
    public override Task can_concurrently_manage_files() => base.can_concurrently_manage_files();

    #endregion

    #region Token paging

    [Fact]
    public override Task token_paging_round_trips_across_serialization() =>
        base.token_paging_round_trips_across_serialization();

    [Fact]
    public override Task list_rejects_malformed_continuation_token() =>
        base.list_rejects_malformed_continuation_token();

    #endregion

    #region Delete by prefix / glob

    [Fact]
    public override Task delete_by_prefix_removes_only_matching_blobs() =>
        base.delete_by_prefix_removes_only_matching_blobs();

    [Fact]
    public override Task can_delete_entire_folder() => base.can_delete_entire_folder();

    [Fact]
    public override Task can_delete_entire_folder_with_wildcard() => base.can_delete_entire_folder_with_wildcard();

    [Fact]
    public override Task can_delete_folder_with_multi_folder_wildcards() =>
        base.can_delete_folder_with_multi_folder_wildcards();

    [Fact]
    public override Task can_delete_specific_files() => base.can_delete_specific_files();

    [Fact]
    public override Task can_delete_nested_folder() => base.can_delete_nested_folder();

    [Fact]
    public override Task can_delete_specific_files_in_nested_folder() =>
        base.can_delete_specific_files_in_nested_folder();

    #endregion

    #region Metadata / Move with metadata

    [Fact]
    public override Task metadata_round_trips_and_sidecar_is_hidden() =>
        base.metadata_round_trips_and_sidecar_is_hidden();

    [Fact]
    public override Task list_metadata_is_opt_in() => base.list_metadata_is_opt_in();

    [Fact]
    public override Task move_relocates_blob_and_metadata() => base.move_relocates_blob_and_metadata();

    #endregion

    #region Normalization round-trip

    [Fact]
    public override Task normalization_round_trips_through_bulk_and_info() =>
        base.normalization_round_trips_through_bulk_and_info();

    #endregion

    #region Bulk operations

    [Fact]
    public override Task bulk_upload_reports_per_blob_results() => base.bulk_upload_reports_per_blob_results();

    [Fact]
    public override Task bulk_upload_failure_does_not_abort_batch() => base.bulk_upload_failure_does_not_abort_batch();

    [Fact]
    public override Task bulk_delete_reports_per_entry_results() => base.bulk_delete_reports_per_entry_results();

    [Fact]
    public override Task bulk_delete_reports_each_blob_by_identity() =>
        base.bulk_delete_reports_each_blob_by_identity();

    #endregion

    #region Container management capability

    [Fact]
    public override Task container_management_capability_matches_support_flag() =>
        base.container_management_capability_matches_support_flag();

    [Fact]
    public override Task container_manager_rejects_traversal_container() =>
        base.container_manager_rejects_traversal_container();

    #endregion

    #region Empty / missing container (no throw)

    [Fact]
    public override Task can_call_delete_all_async_with_empty_container() =>
        base.can_call_delete_all_async_with_empty_container();

    [Fact]
    public override Task can_call_delete_with_empty_container() => base.can_call_delete_with_empty_container();

    [Fact]
    public override Task can_call_bulk_Delete_with_empty_container() =>
        base.can_call_bulk_Delete_with_empty_container();

    [Fact]
    public override Task can_call_move_with_empty_container() => base.can_call_move_with_empty_container();

    [Fact]
    public override Task can_call_copy_with_empty_container() => base.can_call_copy_with_empty_container();

    [Fact]
    public override Task can_call_exists_with_empty_container() => base.can_call_exists_with_empty_container();

    [Fact]
    public override Task can_call_download_with_empty_container() => base.can_call_download_with_empty_container();

    [Fact]
    public override Task can_call_get_blob_info_with_empty_container() =>
        base.can_call_get_blob_info_with_empty_container();

    [Fact]
    public override Task can_call_list_with_empty_container() => base.can_call_list_with_empty_container();

    #endregion

    #region Path traversal & construction security

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\..\\etc\\passwd")]
    [InlineData("subdir/../../../etc/passwd")]
    public override Task blob_location_with_traversal_path_throws(string path) =>
        base.blob_location_with_traversal_path_throws(path);

    [Fact]
    public override Task blob_location_with_traversal_container_throws() =>
        base.blob_location_with_traversal_container_throws();

    [Fact]
    public override Task blob_location_with_control_characters_throws() =>
        base.blob_location_with_control_characters_throws();

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("\\windows\\system32")]
    public override Task blob_location_with_absolute_path_throws(string path) =>
        base.blob_location_with_absolute_path_throws(path);

    [Fact]
    public override Task blob_location_with_reserved_sidecar_suffix_throws() =>
        base.blob_location_with_reserved_sidecar_suffix_throws();

    [Theory]
    [InlineData("../secret/")]
    [InlineData("..\\secret\\")]
    [InlineData("a/../../b")]
    public override Task blob_query_with_traversal_prefix_throws(string prefix) =>
        base.blob_query_with_traversal_prefix_throws(prefix);

    [Fact]
    public override Task blob_query_with_empty_container_throws() => base.blob_query_with_empty_container_throws();

    [Fact]
    public override Task bulk_delete_with_traversal_path_reports_failure() =>
        base.bulk_delete_with_traversal_path_reports_failure();

    #endregion
}
