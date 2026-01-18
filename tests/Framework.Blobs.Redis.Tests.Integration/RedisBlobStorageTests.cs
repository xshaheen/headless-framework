// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Blobs;
using Framework.Blobs.Redis;
using Framework.Serializer;
using Microsoft.Extensions.Options;

// ReSharper disable AccessToDisposedClosure
namespace Tests;

[Collection<RedisBlobStorageFixture>]
public sealed class RedisBlobStorageTests(RedisBlobStorageFixture fixture) : BlobStorageTestsBase
{
    protected override IBlobStorage GetStorage()
    {
        var options = new RedisBlobStorageOptions { ConnectionMultiplexer = fixture.ConnectionMultiplexer };
        var optionsWrapper = new OptionsWrapper<RedisBlobStorageOptions>(options);

        return new RedisBlobStorage(optionsWrapper, new SystemJsonSerializer());
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
    public async Task should_throw_when_blob_exceeds_max_size()
    {
        // given
        var options = new RedisBlobStorageOptions
        {
            ConnectionMultiplexer = fixture.ConnectionMultiplexer,
            MaxBlobSizeBytes = 100, // 100 bytes limit
        };
        var optionsWrapper = new OptionsWrapper<RedisBlobStorageOptions>(options);
        await using var storage = new RedisBlobStorage(optionsWrapper, new SystemJsonSerializer());

        var largeData = new byte[200]; // Exceeds 100 byte limit
        Array.Fill(largeData, (byte)'x');
        await using var stream = new MemoryStream(largeData);

        // when & then
        var act = async () => await storage.UploadAsync(["test-container"], "large-blob.bin", stream);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*exceeds maximum size*");
    }

    [Fact]
    public async Task should_allow_blob_within_size_limit()
    {
        // given
        var options = new RedisBlobStorageOptions
        {
            ConnectionMultiplexer = fixture.ConnectionMultiplexer,
            MaxBlobSizeBytes = 1000,
        };
        var optionsWrapper = new OptionsWrapper<RedisBlobStorageOptions>(options);
        await using var storage = new RedisBlobStorage(optionsWrapper, new SystemJsonSerializer());

        var data = new byte[500]; // Within limit
        Array.Fill(data, (byte)'x');
        await using var stream = new MemoryStream(data);

        // when & then
        Func<Task> act = async () => await storage.UploadAsync(["test-container"], "small-blob.bin", stream);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_skip_size_validation_when_max_size_is_zero()
    {
        // given
        var options = new RedisBlobStorageOptions
        {
            ConnectionMultiplexer = fixture.ConnectionMultiplexer,
            MaxBlobSizeBytes = 0, // Disabled
        };
        var optionsWrapper = new OptionsWrapper<RedisBlobStorageOptions>(options);
        await using var storage = new RedisBlobStorage(optionsWrapper, new SystemJsonSerializer());

        var data = new byte[1000];
        Array.Fill(data, (byte)'x');
        await using var stream = new MemoryStream(data);

        // when & then
        var act = async () => await storage.UploadAsync(["test-container"], "no-limit-blob.bin", stream);
        await act.Should().NotThrowAsync();
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
