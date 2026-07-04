// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Blobs.SshNet;
using Headless.Serializer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet.Common;

namespace Tests;

[Collection<SshBlobStorageFixture>]
public sealed class SshBlobStorageTests(SshBlobStorageFixture fixture) : BlobStorageTestsBase
{
    protected override IBlobStorage GetStorage()
    {
        return new SshBlobStorage(
            fixture.Pool,
            fixture.CrossOsNamingNormalizer,
            new SystemJsonSerializer(),
            fixture.OptionsMonitor,
            TimeProvider.System,
            LoggerFactory.CreateLogger<SshBlobStorage>()
        );
    }

    // SFTP root directories are real containers, so the SSH provider supports container lifecycle through a
    // separately-resolved manager (constructed directly here, never cast from IBlobStorage). It shares the fixture's
    // DI-owned pool and a matching normalizer with GetStorage().
    protected override IBlobContainerManager GetContainerManager() =>
        new SshBlobContainerManager(
            fixture.Pool,
            fixture.CrossOsNamingNormalizer,
            LoggerFactory.CreateLogger<SshBlobContainerManager>()
        );

    #region SSH-specific

    [Fact]
    public async Task can_create_ssh_file_storage_without_Connection_string_password()
    {
        // given
        var options = new SshBlobStorageOptions { ConnectionString = "sftp://headless@localhost:2222" };
        var optionsMonitor = new OptionsMonitorWrapper<SshBlobStorageOptions>(options);

        // when
        await using var storage = new SshBlobStorage(
            fixture.Pool,
            fixture.CrossOsNamingNormalizer,
            new SystemJsonSerializer(),
            optionsMonitor,
            TimeProvider.System,
            LoggerFactory.CreateLogger<SshBlobStorage>()
        );
    }

    [Fact]
    public async Task can_create_ssh_file_storage_without_proxy_password()
    {
        // given
        var options = new SshBlobStorageOptions
        {
            ConnectionString = "sftp://username@host",
            Proxy = "proxy://username@host",
        };
        var optionsMonitor = new OptionsMonitorWrapper<SshBlobStorageOptions>(options);

        // when
        await using var storage = new SshBlobStorage(
            fixture.Pool,
            fixture.CrossOsNamingNormalizer,
            new SystemJsonSerializer(),
            optionsMonitor,
            TimeProvider.System,
            LoggerFactory.CreateLogger<SshBlobStorage>()
        );
    }

    [Fact]
    public async Task will_not_return_directory_in_listing()
    {
        await using var storage = GetStorage();

        await ResetAsync(storage);

        // Create an empty directory directly via the SFTP client. There is no public storage API to materialize an
        // empty folder (the data plane only writes blobs; the container manager normalizes the slash out of a nested
        // name), so this is intentional SSH-specific plumbing for an SSH-specific invariant.
        var client = await fixture.Pool.AcquireAsync(AbortToken);
        try
        {
            await client.CreateDirectoryAsync($"{ContainerName}/EmptyDirectory", AbortToken);
        }
        finally
        {
            await fixture.Pool.ReleaseAsync(client);
        }

        // A directory is never surfaced as a blob by a listing...
        var page = await storage.ListAsync(Container, AbortToken);
        page.Items.Should().BeEmpty();
        page.ContinuationToken.Should().BeNull();
        (await storage.GetBlobsListAsync(Container)).Should().BeEmpty();

        // ...nor by GetBlobInfo, which returns null for a directory (it is not a blob).
        (await storage.GetBlobInfoAsync(new BlobLocation(ContainerName, "EmptyDirectory"), AbortToken))
            .Should()
            .BeNull();
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

    #region Token Paging

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

    [Fact]
    public override Task requires_container_provisioning_reflects_backend_reality() =>
        base.requires_container_provisioning_reflects_backend_reality();

    [Fact]
    public async Task upload_to_missing_container_throws_until_container_manager_ensures_it()
    {
        await using var storage = GetStorage();
        var manager = GetContainerManager();
        var container = "missing-" + Guid.NewGuid().ToString("N");
        var location = new BlobLocation(container, "nested/file.txt");

        try
        {
            var act = async () => await storage.UploadContentAsync(location, "payload", AbortToken);

            await act.Should().ThrowAsync<SftpPathNotFoundException>();
            (await manager.ContainerExistsAsync(container, AbortToken)).Should().BeFalse();

            await manager.EnsureContainerAsync(container, AbortToken);
            await storage.UploadContentAsync(location, "payload", AbortToken);

            (await storage.GetBlobContentAsync(location, AbortToken)).Should().Be("payload");
        }
        finally
        {
            await manager.DeleteContainerAsync(container, AbortToken);
        }
    }

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
}
