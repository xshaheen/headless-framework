// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Headless.Abstractions;
using Headless.Blobs;
using Headless.Blobs.Azure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection<AzureBlobStorageFixture>]
public sealed class AzureStorageTests(AzureBlobStorageFixture fixture) : BlobStorageTestsBase
{
    protected override IBlobStorage GetStorage()
    {
        var blobServiceClient = new BlobServiceClient(
            fixture.Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );

        var azureStorageOptions = new AzureStorageOptions();
        var optionsAccessor = new OptionsWrapper<AzureStorageOptions>(azureStorageOptions);
        var mimeTypeProvider = new MimeTypeProvider();
        var clock = new Clock(TimeProvider.System);
        var normalizer = new AzureBlobNamingNormalizer();

        return new AzureBlobStorage(
            blobServiceClient,
            mimeTypeProvider,
            clock,
            optionsAccessor,
            normalizer,
            LoggerFactory.CreateLogger<AzureBlobStorage>()
        );
    }

    [Fact]
    public async Task can_round_trip_via_presigned_download_url()
    {
        var storage = (IPresignedUrlBlobStorage)GetStorage();
        var container = new[] { $"presign{Guid.NewGuid():N}" };
        var content = "presigned-content"u8.ToArray();

        using (var stream = new MemoryStream(content))
        {
            await ((IBlobStorage)storage).UploadAsync(container, "file.txt", stream, cancellationToken: AbortToken);
        }

        var url = await storage.GetPresignedDownloadUrlAsync(
            container,
            "file.txt",
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        using var http = new HttpClient();
        var downloaded = await http.GetByteArrayAsync(url, AbortToken);

        downloaded.Should().Equal(content);
    }

    [Fact]
    public async Task can_round_trip_via_presigned_upload_url()
    {
        var storage = (IPresignedUrlBlobStorage)GetStorage();
        var container = new[] { $"presign{Guid.NewGuid():N}" };
        var content = "presigned-upload"u8.ToArray();

        // The presigned PUT goes straight to Azure and does not create the container; create it first.
        await ((IBlobStorage)storage).CreateContainerAsync(container, AbortToken);

        var uploadUrl = await storage.GetPresignedUploadUrlAsync(
            container,
            "file.txt",
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        using (var http = new HttpClient())
        using (var body = new ByteArrayContent(content))
        {
            // Azure block-blob PUT requires the blob-type header.
            body.Headers.Add("x-ms-blob-type", "BlockBlob");
            var response = await http.PutAsync(uploadUrl, body, AbortToken);
            response.EnsureSuccessStatusCode();
        }

        var readBack = await ((IBlobStorage)storage).OpenReadStreamAsync(container, "file.txt", AbortToken);
        readBack.Should().NotBeNull();

        using var buffer = new MemoryStream();
        await readBack!.Stream.CopyToAsync(buffer, AbortToken);
        buffer.ToArray().Should().Equal(content);
    }

    [Fact]
    public async Task delete_all_async_removes_every_page_when_results_span_multiple_pages()
    {
        // Regression: the previous do/while loop advanced before deleting, so the final page (loaded by the last
        // NextPageAsync) was never deleted. Exercise the multi-page path with a tiny page size instead of 500+ blobs.
        await using var storage = (AzureBlobStorage)GetStorage();
        var name = $"c{Guid.NewGuid():N}";
        string[] container = [name, "bulk"];

        const int total = 5;

        for (var i = 0; i < total; i++)
        {
            using var content = new MemoryStream("x"u8.ToArray());
            await storage.UploadAsync(container, $"f{i}.txt", content, cancellationToken: AbortToken);
        }

        // pageSize=2 over 5 blobs => 3 pages; the bug left the last page undeleted and undercounted.
        var deleted = await storage.DeleteAllAsync(container, blobSearchPattern: null, pageSize: 2, AbortToken);

        deleted.Should().Be(total);
        (await storage.GetBlobsListAsync(container)).Should().BeEmpty();
    }

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
    public override Task can_get_paged_file_list_for_single_folder()
    {
        return base.can_get_paged_file_list_for_single_folder();
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
    public override Task can_rename_files()
    {
        return base.can_rename_files();
    }

    [Fact]
    public override Task can_concurrently_manage_files()
    {
        return base.can_concurrently_manage_files();
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
    public override Task can_call_delete_all_async_with_empty_container()
    {
        return base.can_call_delete_all_async_with_empty_container();
    }

    [Fact]
    public override Task can_call_copy_with_empty_container()
    {
        return base.can_call_copy_with_empty_container();
    }

    [Fact]
    public override Task can_call_rename_with_empty_container()
    {
        return base.can_call_rename_with_empty_container();
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
    public override Task can_call_get_paged_list_with_empty_container()
    {
        return base.can_call_get_paged_list_with_empty_container();
    }

    [Fact]
    public override Task bulk_upload_reports_per_blob_results()
    {
        return base.bulk_upload_reports_per_blob_results();
    }

    [Fact]
    public override Task bulk_upload_aligns_results_to_input_order_under_failures()
    {
        return base.bulk_upload_aligns_results_to_input_order_under_failures();
    }

    [Fact]
    public override Task delete_all_with_empty_container_array_throws()
    {
        return base.delete_all_with_empty_container_array_throws();
    }

    // bulk_delete_reports_per_entry_results / bulk_delete_aligns_results_to_input_order are intentionally NOT wired
    // here: the Azure batch delete (and the Azurite emulator used in tests) report success for already-absent
    // blobs, so the "not found -> Ok(false)" distinction those tests assert is not reliable. See
    // IBlobStorage.BulkDeleteAsync remarks.

    #region Path Traversal Security Tests

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\..\\etc\\passwd")]
    [InlineData("subdir/../../../etc/passwd")]
    public override Task should_throw_when_blob_name_has_path_traversal(string blobName)
    {
        return base.should_throw_when_blob_name_has_path_traversal(blobName);
    }

    [Fact]
    public override Task should_throw_when_container_has_path_traversal()
    {
        return base.should_throw_when_container_has_path_traversal();
    }

    [Fact]
    public override Task should_throw_when_upload_blob_has_path_traversal()
    {
        return base.should_throw_when_upload_blob_has_path_traversal();
    }

    [Fact]
    public override Task should_throw_when_download_blob_has_path_traversal()
    {
        return base.should_throw_when_download_blob_has_path_traversal();
    }

    [Fact]
    public override Task should_throw_when_delete_blob_has_path_traversal()
    {
        return base.should_throw_when_delete_blob_has_path_traversal();
    }

    [Fact]
    public override Task should_throw_when_rename_source_blob_has_path_traversal()
    {
        return base.should_throw_when_rename_source_blob_has_path_traversal();
    }

    [Fact]
    public override Task should_throw_when_copy_source_blob_has_path_traversal()
    {
        return base.should_throw_when_copy_source_blob_has_path_traversal();
    }

    [Fact]
    public override Task should_throw_when_blob_name_has_control_characters()
    {
        return base.should_throw_when_blob_name_has_control_characters();
    }

    [Theory]
    [InlineData("/etc/passwd")]
    public override Task should_throw_when_blob_name_is_absolute_path(string blobName)
    {
        return base.should_throw_when_blob_name_is_absolute_path(blobName);
    }

    #endregion
}
