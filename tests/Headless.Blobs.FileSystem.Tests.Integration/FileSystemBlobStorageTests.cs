// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Blobs.FileSystem;
using Headless.Serializer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class FileSystemBlobStorageTests : BlobStorageTestsBase
{
    private readonly string _baseDirectoryPath = Directory.CreateTempSubdirectory().FullName;

    private IOptions<FileSystemBlobStorageOptions> _Options =>
        new OptionsWrapper<FileSystemBlobStorageOptions>(
            new FileSystemBlobStorageOptions { BaseDirectoryPath = _baseDirectoryPath }
        );

    protected override IBlobStorage GetStorage()
    {
        return new FileSystemBlobStorage(
            _Options,
            new SystemJsonSerializer(new DefaultJsonOptionsProvider()),
            new CrossOsNamingNormalizer(),
            TimeProvider.System,
            NullLogger<FileSystemBlobStorage>.Instance
        );
    }

    // The file-system backend supports container lifecycle: a top-level container is a directory directly under the
    // base path. The capability is resolved (constructed), never cast from IBlobStorage (KTD5).
    protected override IBlobContainerManager GetContainerManager()
    {
        return new FileSystemBlobContainerManager(_Options, new CrossOsNamingNormalizer());
    }

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

    // Glob matching is now a client-side filter over a full recursive enumeration (ListAsync returns every blob and
    // the shared matcher tests whole keys), so multi-folder wildcards work on the file system — unlike the old
    // server-side Directory.EnumerateFiles model that could not span nested folders.
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

    [Fact]
    public override Task bulk_delete_reports_per_entry_results()
    {
        return base.bulk_delete_reports_per_entry_results();
    }

    [Fact]
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
        var container = "missing-" + Guid.NewGuid().ToString("N");
        var location = new BlobLocation(container, "nested/file.txt");

        var act = async () => await storage.UploadContentAsync(location, "payload", AbortToken);

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
        (await manager.ContainerExistsAsync(container, AbortToken)).Should().BeFalse();

        await manager.EnsureContainerAsync(container, AbortToken);
        await storage.UploadContentAsync(location, "payload", AbortToken);

        (await storage.GetBlobContentAsync(location, AbortToken)).Should().Be("payload");
    }

    [Fact]
    public async Task container_manager_rejects_container_that_normalizes_to_storage_root()
    {
        var manager = GetContainerManager();

        Directory.Exists(_baseDirectoryPath).Should().BeTrue();

        var ensure = () => manager.EnsureContainerAsync(":", AbortToken).AsTask();
        var exists = () => manager.ContainerExistsAsync(":", AbortToken).AsTask();
        var delete = () => manager.DeleteContainerAsync(":", AbortToken).AsTask();

        await ensure.Should().ThrowAsync<ArgumentException>();
        await exists.Should().ThrowAsync<ArgumentException>();
        await delete.Should().ThrowAsync<ArgumentException>();

        Directory.Exists(_baseDirectoryPath).Should().BeTrue();
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

    #region File-system specific behavior

    [Fact]
    public async Task directories_are_never_returned_in_listings_or_blob_info()
    {
        await using var storage = GetStorage();
        await ResetAsync(storage);

        // An empty directory under the container is a backing-store artifact, never a listable blob.
        var emptyDirectory = Path.Combine(_baseDirectoryPath, ContainerName, "EmptyDirectory");
        Directory.CreateDirectory(emptyDirectory);

        (await storage.GetBlobsListAsync(Container)).Should().BeEmpty();
        (await storage.GetBlobInfoAsync(new BlobLocation(ContainerName, "EmptyDirectory"), AbortToken))
            .Should()
            .BeNull("a directory is not a blob");

        // A nested file is listed by its key; the directory that holds it is not a separate entry.
        await storage.UploadContentAsync(new BlobLocation(ContainerName, "folder", "file.txt"), "x", AbortToken);

        var listed = await storage.GetBlobsListAsync(Container);
        listed.Should().ContainSingle();
        listed[0].BlobKey.Should().Be("folder/file.txt");
    }

    [Fact]
    public async Task delete_all_preserves_container_directory()
    {
        await using var storage = GetStorage();
        await ResetAsync(storage);

        await storage.UploadContentAsync(new BlobLocation(ContainerName, "sub", "a.txt"), "a", AbortToken);

        var containerDirectory = Path.Combine(_baseDirectoryPath, ContainerName);
        Directory.Exists(containerDirectory).Should().BeTrue();

        await storage.DeleteAllAsync(Container, AbortToken);

        // DeleteAll is a data-plane operation: it removes blobs (and their sidecars), not the container itself.
        // Container lifecycle belongs to FileSystemBlobContainerManager, so the container root survives a delete-all.
        Directory.Exists(containerDirectory).Should().BeTrue();
        (await storage.GetBlobsListAsync(Container)).Should().BeEmpty();
    }

    #endregion
}
