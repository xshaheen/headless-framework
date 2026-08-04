// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Blobs.SshNet;
using Headless.Hosting.Options;
using Headless.Serializer;
using Microsoft.Extensions.Logging;

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
    protected override IBlobContainerManager GetContainerManager()
    {
        return new SshBlobContainerManager(
            fixture.Pool,
            fixture.CrossOsNamingNormalizer,
            LoggerFactory.CreateLogger<SshBlobContainerManager>()
        );
    }

    #region SSH-specific

    [Fact]
    public async Task can_create_ssh_file_storage_without_connection_string_password()
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
        (await storage.GetBlobsListAsync(Container, cancellationToken: AbortToken)).Should().BeEmpty();

        // ...nor by GetBlobInfo, which returns null for a directory (it is not a blob).
        (await storage.GetBlobInfoAsync(new BlobLocation(ContainerName, "EmptyDirectory"), AbortToken))
            .Should()
            .BeNull();
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
    public override Task requires_container_provisioning_reflects_backend_reality()
    {
        return base.requires_container_provisioning_reflects_backend_reality();
    }

    [Fact]
    public async Task upload_to_unprovisioned_container_throws_in_sftp_test_fixture()
    {
        await using var storage = GetStorage();
        var manager = GetContainerManager();
        var container = "missing-" + Guid.NewGuid().ToString("N");
        var location = new BlobLocation(container, "nested/file.txt");

        try
        {
            var act = async () => await storage.UploadContentAsync(location, "payload", AbortToken);

            // DirectoryNotFoundException, not the SSH.NET SftpPathNotFoundException: a never-provisioned container
            // is a provisioning error, and it carries the same type here as on the FileSystem provider so
            // provider-portable code can catch one exception for both.
            await act.Should().ThrowAsync<DirectoryNotFoundException>();
            (await manager.ContainerExistsAsync(container, AbortToken)).Should().BeFalse();
        }
        finally
        {
            await manager.DeleteContainerAsync(container, AbortToken);
        }
    }

    [Fact]
    public async Task copy_to_missing_container_throws_instead_of_reporting_missing_source()
    {
        await using var storage = GetStorage();
        await ResetAsync(storage);

        var manager = GetContainerManager();
        var source = new BlobLocation(ContainerName, "copy-source.txt");
        await storage.UploadContentAsync(source, "payload", AbortToken);

        var missingContainer = "missing-" + Guid.NewGuid().ToString("N");
        var destination = new BlobLocation(missingContainer, "copy-target.txt");

        try
        {
            var act = async () => await storage.CopyAsync(source, destination, AbortToken);

            // Copy is a data-plane operation: like UploadAsync it must refuse — not silently provision — a
            // destination container that was never ensured. Returning false here would claim the source is
            // missing (the documented meaning of false) while it demonstrably exists.
            await act.Should().ThrowAsync<DirectoryNotFoundException>();
            (await manager.ContainerExistsAsync(missingContainer, AbortToken)).Should().BeFalse();
            (await storage.ExistsAsync(source, AbortToken)).Should().BeTrue();
        }
        finally
        {
            await manager.DeleteContainerAsync(missingContainer, AbortToken);
        }
    }

    [Fact]
    public async Task move_to_missing_container_throws_and_keeps_the_source()
    {
        await using var storage = GetStorage();
        await ResetAsync(storage);

        var manager = GetContainerManager();
        var source = new BlobLocation(ContainerName, "move-source.txt");
        await storage.UploadContentAsync(source, "payload", AbortToken);

        var missingContainer = "missing-" + Guid.NewGuid().ToString("N");
        var destination = new BlobLocation(missingContainer, "move-target.txt");

        try
        {
            var act = async () => await storage.MoveAsync(source, destination, AbortToken);

            // Move funnels through CopyAsync, so the provisioning error propagates instead of being swallowed as a
            // "source not found" false — and the source survives because the copy never ran.
            await act.Should().ThrowAsync<DirectoryNotFoundException>();
            (await manager.ContainerExistsAsync(missingContainer, AbortToken)).Should().BeFalse();
            (await storage.GetBlobContentAsync(source, AbortToken)).Should().Be("payload");
        }
        finally
        {
            await manager.DeleteContainerAsync(missingContainer, AbortToken);
        }
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
    [InlineData("..\\escape\\")]
    [InlineData("foo/../bar")]
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
