// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Abstractions;
using Headless.Blobs;
using Headless.Blobs.Azure;
using Headless.Blobs.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection<AzureBlobStorageFixture>]
public sealed class AzureStorageTests(AzureBlobStorageFixture fixture) : BlobStorageTestsBase
{
    private BlobServiceClient _CreateClient()
    {
        return new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );
    }

    protected override IBlobStorage GetStorage()
    {
        return new AzureBlobStorage(
            _CreateClient(),
            new MimeTypeProvider(),
            new Clock(TimeProvider.System),
            new OptionsWrapper<AzureStorageOptions>(new AzureStorageOptions()),
            new AzureBlobNamingNormalizer(),
            LoggerFactory.CreateLogger<AzureBlobStorage>()
        );
    }

    // Azure supports container lifecycle: the capability is a separately-resolved AzureBlobContainerManager sharing
    // the storage's Azurite endpoint (a fresh client over the same connection string), never a cast from IBlobStorage.
    protected override IBlobContainerManager GetContainerManager()
    {
        return new AzureBlobContainerManager(_CreateClient(), new AzureBlobNamingNormalizer(), PublicAccessType.None);
    }

    #region Azure-specific: presigned URLs

    [Fact]
    public async Task can_round_trip_via_presigned_download_url()
    {
        await using var storage = GetStorage();
        var presigned = (IPresignedUrlBlobStorage)storage;
        var manager = GetContainerManager();

        var container = $"presign{Guid.NewGuid():N}";
        var location = new BlobLocation(container, "file.txt");
        var content = "presigned-content"u8.ToArray();

        await manager.EnsureContainerAsync(container, AbortToken);

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
        var manager = GetContainerManager();

        var container = $"presign{Guid.NewGuid():N}";
        var location = new BlobLocation(container, "file.txt");
        var content = "presigned-upload"u8.ToArray();

        // The presigned PUT goes straight to Azure and does not create the container; ensure it first.
        await manager.EnsureContainerAsync(container, AbortToken);

        var uploadUrl = await presigned.GetPresignedUploadUrlAsync(location, TimeSpan.FromMinutes(5), AbortToken);

        using (var http = new HttpClient())
        using (var body = new ByteArrayContent(content))
        {
            // Azure block-blob PUT requires the blob-type header.
            body.Headers.Add("x-ms-blob-type", "BlockBlob");
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

    #region Azure-specific: delete-all paging

    [Fact]
    public async Task delete_all_async_removes_every_page_when_results_span_multiple_pages()
    {
        // Regression: an earlier do/while loop advanced before deleting, so the final page was never deleted. Exercise
        // the multi-page path with a tiny page size instead of 500+ blobs.
        await using var storage = GetStorage();
        var manager = GetContainerManager();

        var name = $"c{Guid.NewGuid():N}";
        await manager.EnsureContainerAsync(name, AbortToken);

        const int total = 5;

        for (var i = 0; i < total; i++)
        {
            using var content = new MemoryStream("x"u8.ToArray());
            await storage.UploadAsync(
                new BlobLocation(name, "bulk", $"f{i}.txt"),
                content,
                cancellationToken: AbortToken
            );
        }

        // pageSize=2 over 5 blobs => 3 native pages; the bug left the last page undeleted and undercounted.
        var deleted = await storage.DeleteAllAsync(new BlobQuery(name, prefix: null, pageSize: 2), AbortToken);

        deleted.Should().Be(total);
        (await storage.GetBlobsListAsync(new BlobQuery(name))).Should().BeEmpty();
    }

    [Fact]
    public async Task delete_all_clears_raw_backend_keys_that_blob_location_rejects()
    {
        // B1 regression: a blob written out-of-band whose name BlobLocation rejects (the reserved sidecar suffix)
        // must not make the container permanently un-clearable — DeleteAllAsync deletes the listed backend keys
        // directly instead of re-wrapping them in BlobLocation.
        await using var storage = GetStorage();
        var manager = GetContainerManager();

        var container = $"rawclear{Guid.NewGuid():N}";
        await manager.EnsureContainerAsync(container, AbortToken);

        // A framework-written blob plus a raw, backend-legal but BlobLocation-illegal key written natively.
        await storage.UploadContentAsync(new BlobLocation(container, "normal.txt"), "data", AbortToken);

        var containerClient = _CreateClient().GetBlobContainerClient(container);
        var rawKey = "out-of-band" + BlobStorageHelpers.SidecarSuffix;
        await containerClient
            .GetBlobClient(rawKey)
            .UploadAsync(BinaryData.FromString("raw"), overwrite: false, AbortToken);

        var deleted = await storage.DeleteAllAsync(new BlobQuery(container), AbortToken);

        deleted.Should().Be(2);

        // Verify through the native client: nothing at all is left behind, including the BlobLocation-illegal key.
        var remaining = 0;

        await foreach (var _ in containerClient.GetBlobsAsync(cancellationToken: AbortToken))
        {
            remaining++;
        }

        remaining.Should().Be(0);
    }

    #endregion

    #region List / Round-trip

    [Fact]
    public override Task can_get_empty_file_list_on_missing_directory()
    {
        return base.can_get_empty_file_list_on_missing_directory();
    }

    [Fact]
    public override Task can_get_file_list_for_single_folder()
    {
        return base.can_get_file_list_for_single_folder();
    }

    [Fact]
    public override Task can_get_file_list_for_single_file()
    {
        return base.can_get_file_list_for_single_file();
    }

    [Fact]
    public override Task can_get_file_info()
    {
        return base.can_get_file_info();
    }

    [Fact]
    public override Task can_get_non_existent_file_info()
    {
        return base.can_get_non_existent_file_info();
    }

    [Fact]
    public override Task can_manage_files()
    {
        return base.can_manage_files();
    }

    [Fact]
    public override Task can_move_files()
    {
        return base.can_move_files();
    }

    [Fact]
    public override Task can_round_trip_seekable_stream()
    {
        return base.can_round_trip_seekable_stream();
    }

    [Fact]
    public override Task will_reset_stream_position()
    {
        return base.will_reset_stream_position();
    }

    [Fact]
    public override Task can_save_over_existing_stored_content()
    {
        return base.can_save_over_existing_stored_content();
    }

    [Fact]
    public override Task can_concurrently_manage_files()
    {
        return base.can_concurrently_manage_files();
    }

    #endregion

    #region Token Paging

    [Fact]
    public override Task token_paging_round_trips_across_serialization()
    {
        return base.token_paging_round_trips_across_serialization();
    }

    [Fact]
    public override Task list_rejects_malformed_continuation_token()
    {
        return base.list_rejects_malformed_continuation_token();
    }

    #endregion

    #region Delete by prefix / glob

    [Fact]
    public override Task delete_by_prefix_removes_only_matching_blobs()
    {
        return base.delete_by_prefix_removes_only_matching_blobs();
    }

    [Fact]
    public override Task can_delete_entire_folder()
    {
        return base.can_delete_entire_folder();
    }

    [Fact]
    public override Task can_delete_entire_folder_with_wildcard()
    {
        return base.can_delete_entire_folder_with_wildcard();
    }

    [Fact]
    public override Task can_delete_folder_with_multi_folder_wildcards()
    {
        return base.can_delete_folder_with_multi_folder_wildcards();
    }

    [Fact]
    public override Task can_delete_specific_files()
    {
        return base.can_delete_specific_files();
    }

    [Fact]
    public override Task can_delete_nested_folder()
    {
        return base.can_delete_nested_folder();
    }

    [Fact]
    public override Task can_delete_specific_files_in_nested_folder()
    {
        return base.can_delete_specific_files_in_nested_folder();
    }

    #endregion

    #region Metadata / Move with metadata

    [Fact]
    public override Task metadata_round_trips_and_sidecar_is_hidden()
    {
        return base.metadata_round_trips_and_sidecar_is_hidden();
    }

    [Fact]
    public override Task list_metadata_is_opt_in()
    {
        return base.list_metadata_is_opt_in();
    }

    [Fact]
    public override Task move_relocates_blob_and_metadata()
    {
        return base.move_relocates_blob_and_metadata();
    }

    #endregion

    #region Normalization round-trip

    [Fact]
    public override Task normalization_round_trips_through_bulk_and_info()
    {
        return base.normalization_round_trips_through_bulk_and_info();
    }

    #endregion

    #region Bulk operations

    [Fact]
    public override Task bulk_upload_reports_per_blob_results()
    {
        return base.bulk_upload_reports_per_blob_results();
    }

    [Fact]
    public override Task bulk_upload_failure_does_not_abort_batch()
    {
        return base.bulk_upload_failure_does_not_abort_batch();
    }

    [Fact(
        Skip = "Azure batch delete reports already-absent blobs as success in Azurite, so Ok(false) is not observable."
    )]
    public override Task bulk_delete_reports_per_entry_results()
    {
        return base.bulk_delete_reports_per_entry_results();
    }

    [Fact(
        Skip = "Azure batch delete reports already-absent blobs as success in Azurite, so Ok(false) is not observable."
    )]
    public override Task bulk_delete_reports_each_blob_by_identity()
    {
        return base.bulk_delete_reports_each_blob_by_identity();
    }

    #endregion

    #region Container management capability

    [Fact]
    public override Task container_management_capability_matches_support_flag()
    {
        return base.container_management_capability_matches_support_flag();
    }

    [Fact]
    public override Task container_manager_rejects_traversal_container()
    {
        return base.container_manager_rejects_traversal_container();
    }

    [Fact]
    public async Task upload_to_missing_container_throws_until_container_manager_ensures_it()
    {
        await using var storage = GetStorage();
        var manager = GetContainerManager();
        var container = "missing" + Guid.NewGuid().ToString("N");
        var location = new BlobLocation(container, "nested/file.txt");

        var act = async () => await storage.UploadContentAsync(location, "payload", AbortToken);

        await act.Should().ThrowAsync<RequestFailedException>();
        (await manager.ContainerExistsAsync(container, AbortToken)).Should().BeFalse();

        await manager.EnsureContainerAsync(container, AbortToken);
        await storage.UploadContentAsync(location, "payload", AbortToken);
        (await storage.GetBlobContentAsync(location, AbortToken)).Should().Be("payload");
    }

    #endregion

    #region Empty / missing container (no throw)

    [Fact]
    public override Task can_call_delete_all_async_with_empty_container()
    {
        return base.can_call_delete_all_async_with_empty_container();
    }

    [Fact]
    public override Task can_call_delete_with_empty_container()
    {
        return base.can_call_delete_with_empty_container();
    }

    [Fact]
    public override Task can_call_bulk_Delete_with_empty_container()
    {
        return base.can_call_bulk_Delete_with_empty_container();
    }

    [Fact]
    public override Task can_call_move_with_empty_container()
    {
        return base.can_call_move_with_empty_container();
    }

    [Fact]
    public override Task can_call_copy_with_empty_container()
    {
        return base.can_call_copy_with_empty_container();
    }

    [Fact]
    public override Task can_call_exists_with_empty_container()
    {
        return base.can_call_exists_with_empty_container();
    }

    [Fact]
    public override Task can_call_download_with_empty_container()
    {
        return base.can_call_download_with_empty_container();
    }

    [Fact]
    public override Task can_call_get_blob_info_with_empty_container()
    {
        return base.can_call_get_blob_info_with_empty_container();
    }

    [Fact]
    public override Task can_call_list_with_empty_container()
    {
        return base.can_call_list_with_empty_container();
    }

    #endregion

    #region Path Traversal & Construction Security Tests

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\..\\etc\\passwd")]
    [InlineData("subdir/../../../etc/passwd")]
    public override Task blob_location_with_traversal_path_throws(string path)
    {
        return base.blob_location_with_traversal_path_throws(path);
    }

    [Fact]
    public override Task blob_location_with_traversal_container_throws()
    {
        return base.blob_location_with_traversal_container_throws();
    }

    [Fact]
    public override Task blob_location_with_control_characters_throws()
    {
        return base.blob_location_with_control_characters_throws();
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("\\windows\\system32")]
    public override Task blob_location_with_absolute_path_throws(string path)
    {
        return base.blob_location_with_absolute_path_throws(path);
    }

    [Fact]
    public override Task blob_location_with_reserved_sidecar_suffix_throws()
    {
        return base.blob_location_with_reserved_sidecar_suffix_throws();
    }

    [Theory]
    [InlineData("../escape/")]
    [InlineData("..\\escape")]
    [InlineData("nested/../escape")]
    public override Task blob_query_with_traversal_prefix_throws(string prefix)
    {
        return base.blob_query_with_traversal_prefix_throws(prefix);
    }

    [Fact]
    public override Task blob_query_with_empty_container_throws()
    {
        return base.blob_query_with_empty_container_throws();
    }

    [Fact]
    public override Task bulk_delete_with_traversal_path_reports_failure()
    {
        return base.bulk_delete_with_traversal_path_reports_failure();
    }

    #endregion
}
