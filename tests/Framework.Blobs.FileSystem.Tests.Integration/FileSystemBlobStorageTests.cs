// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Blobs;
using Framework.Blobs.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class FileSystemBlobStorageTests : BlobStorageTestsBase
{
    private readonly string _baseDirectoryPath = Directory.CreateTempSubdirectory().FullName;

    protected override IBlobStorage GetStorage()
    {
        var options = new FileSystemBlobStorageOptions { BaseDirectoryPath = _baseDirectoryPath };
        var optionsWrapper = new OptionsWrapper<FileSystemBlobStorageOptions>(options);
        var logger = NullLogger<FileSystemBlobStorage>.Instance;
        var normalizer = new CrossOsNamingNormalizer();

        return new FileSystemBlobStorage(optionsWrapper, normalizer, logger);
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

    [Fact(Skip = "Directory.EnumerateFiles does not support nested folder wildcards")]
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
    public async Task WillNotReturnDirectoryInGetPagedFileListAsync()
    {
        var container = Container;
        var containerName = ContainerName;
        await using var storage = (FileSystemBlobStorage)GetStorage();
        await ResetAsync(storage);

        var result = await storage.GetPagedListAsync(container, cancellationToken: AbortToken);
        result.HasMore.Should().BeFalse();
        result.Blobs.Should().BeEmpty();
        (await result.NextPageAsync(AbortToken)).Should().BeFalse();
        result.HasMore.Should().BeFalse();
        result.Blobs.Should().BeEmpty();

        const string directory = "EmptyDirectory/";
        Directory.CreateDirectory(Path.Combine(_baseDirectoryPath, containerName, directory));

        result = await storage.GetPagedListAsync(container, cancellationToken: AbortToken);
        result.HasMore.Should().BeFalse();
        result.Blobs.Should().BeEmpty();
        (await result.NextPageAsync(AbortToken)).Should().BeFalse();
        result.HasMore.Should().BeFalse();
        result.Blobs.Should().BeEmpty();

        // Ensure the directory will not be returned via get file info
        var info = await storage.GetBlobInfoAsync(container, directory, AbortToken);
        info.Should().BeNull();

        // Ensure delete files can remove all files including fake folders
        await storage.DeleteAllAsync(container, "*", AbortToken);

        // Assert folder was removed by Delete Files
        Directory.Exists(Path.Combine(_baseDirectoryPath, containerName, directory)).Should().BeFalse();

        info = await storage.GetBlobInfoAsync(container, directory, AbortToken);
        info.Should().BeNull();
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
