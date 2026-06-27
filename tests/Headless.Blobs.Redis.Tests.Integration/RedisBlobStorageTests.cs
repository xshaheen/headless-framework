// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Blobs.Redis;
using Headless.Serializer;
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

        return new RedisBlobStorage(
            optionsWrapper,
            new SystemJsonSerializer(),
            new CrossOsNamingNormalizer(),
            TimeProvider.System
        );
    }

    // Redis supports container lifecycle through a separately-resolved manager (the manager is constructed directly
    // here, never cast from IBlobStorage). It shares the fixture's multiplexer and a matching normalizer so it targets
    // the same backing hashes as GetStorage().
    protected override IBlobContainerManager GetContainerManager() =>
        new RedisBlobContainerManager(fixture.ConnectionMultiplexer, new CrossOsNamingNormalizer());

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

    #region Token Paging

    [Fact]
    public override Task token_paging_round_trips_across_serialization() =>
        base.token_paging_round_trips_across_serialization();

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

    [Fact]
    public async Task delete_all_treats_prefix_glob_metacharacters_as_literals()
    {
        await using var storage = GetStorage();
        await ResetAsync(storage);

        var literal = new BlobLocation(ContainerName, "tenant[1]", "a.txt");
        var globMatch = new BlobLocation(ContainerName, "tenant1", "a.txt");

        await storage.UploadContentAsync(literal, "literal", AbortToken);
        await storage.UploadContentAsync(globMatch, "glob", AbortToken);

        var deleted = await storage.DeleteAllAsync(new BlobQuery(ContainerName, "tenant[1]/"), AbortToken);

        deleted.Should().Be(1);
        (await storage.ExistsAsync(literal, AbortToken)).Should().BeFalse();
        (await storage.GetBlobContentAsync(globMatch, AbortToken)).Should().Be("glob");
    }

    #endregion

    #region Metadata / Move with metadata

    [Fact]
    public override Task metadata_round_trips_and_sidecar_is_hidden() =>
        base.metadata_round_trips_and_sidecar_is_hidden();

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

    #region Path Traversal & Construction Security Tests

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
    [InlineData("../escape/")]
    [InlineData("..\\escape\\")]
    [InlineData("foo/../bar")]
    public override Task blob_query_with_traversal_prefix_throws(string prefix) =>
        base.blob_query_with_traversal_prefix_throws(prefix);

    [Fact]
    public override Task blob_query_with_empty_container_throws() => base.blob_query_with_empty_container_throws();

    [Fact]
    public override Task bulk_delete_with_traversal_path_reports_failure() =>
        base.bulk_delete_with_traversal_path_reports_failure();

    #endregion

    #region Redis-specific: blob size limit (small/ephemeral-blob guard)

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
        await using var storage = new RedisBlobStorage(
            optionsWrapper,
            new SystemJsonSerializer(),
            new CrossOsNamingNormalizer(),
            TimeProvider.System
        );

        var largeData = new byte[200]; // Exceeds 100 byte limit
        Array.Fill(largeData, (byte)'x');
        await using var stream = new MemoryStream(largeData);

        // when & then
        var act = async () =>
            await storage.UploadAsync(
                new BlobLocation("test-container", "large-blob.bin"),
                stream,
                cancellationToken: AbortToken
            );
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
        await using var storage = new RedisBlobStorage(
            optionsWrapper,
            new SystemJsonSerializer(),
            new CrossOsNamingNormalizer(),
            TimeProvider.System
        );

        var data = new byte[500]; // Within limit
        Array.Fill(data, (byte)'x');
        await using var stream = new MemoryStream(data);

        // when & then
        var act = async () =>
            await storage.UploadAsync(
                new BlobLocation("test-container", "small-blob.bin"),
                stream,
                cancellationToken: AbortToken
            );
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
        await using var storage = new RedisBlobStorage(
            optionsWrapper,
            new SystemJsonSerializer(),
            new CrossOsNamingNormalizer(),
            TimeProvider.System
        );

        var data = new byte[1000];
        Array.Fill(data, (byte)'x');
        await using var stream = new MemoryStream(data);

        // when & then
        var act = async () =>
            await storage.UploadAsync(
                new BlobLocation("test-container", "no-limit-blob.bin"),
                stream,
                cancellationToken: AbortToken
            );
        await act.Should().NotThrowAsync();
    }

    #endregion
}
