// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Runs the blob storage conformance suite against a FileSystem store wrapped by tenant scoping, proving the wrapper
/// keeps the whole <see cref="IBlobStorage"/> contract while every blob lands under the tenant's path prefix.
/// </summary>
public sealed class TenantScopedFileSystemBlobStorageTests : BlobStorageTestsBase
{
    private const string _Tenant = "tenant-a";

    private readonly string _baseDirectoryPath = Directory.CreateTempSubdirectory().FullName;
    private readonly List<ServiceProvider> _providers = [];

    protected override IBlobStorage GetStorage()
    {
        return _Build(_Tenant).GetRequiredService<IBlobStorage>();
    }

    // The path-prefix strategy shares containers between tenants, so container lifecycle stays unscoped.
    protected override IBlobContainerManager GetContainerManager()
    {
        return _Build(_Tenant).GetRequiredService<IBlobContainerManager>();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_keep_tenants_apart_on_one_physical_store()
    {
        // given
        var location = new BlobLocation(ContainerName, "avatars/1.png");
        await GetContainerManager().EnsureContainerAsync(ContainerName, AbortToken);
        var tenantA = _Build("tenant-a").GetRequiredService<IBlobStorage>();
        var tenantB = _Build("tenant-b").GetRequiredService<IBlobStorage>();

        await tenantA.UploadContentAsync(location, "a", AbortToken);
        await tenantB.UploadContentAsync(location, "b", AbortToken);

        // when
        var deletedByA = await tenantA.DeleteAllAsync(new BlobQuery(ContainerName), AbortToken);

        // then
        deletedByA.Should().Be(1);
        (await tenantA.ExistsAsync(location, AbortToken)).Should().BeFalse();
        (await tenantB.GetBlobContentAsync(location, AbortToken)).Should().Be("b");
        (await tenantB.GetBlobsListAsync(new BlobQuery(ContainerName), cancellationToken: AbortToken))
            .Select(blob => blob.BlobKey)
            .Should()
            .Equal("avatars/1.png");
        File.Exists(Path.Combine(_baseDirectoryPath, ContainerName, "tenant-b", "avatars", "1.png")).Should().BeTrue();
    }

    [Fact]
    public async Task should_reach_the_physical_location_only_through_the_bypass()
    {
        // given
        var provider = _Build(_Tenant);
        var storage = provider.GetRequiredService<IBlobStorage>();
        await GetContainerManager().EnsureContainerAsync(ContainerName, AbortToken);
        await storage.UploadContentAsync(new BlobLocation(ContainerName, "1.txt"), "scoped", AbortToken);

        // when
        string? physical;

        using (provider.GetRequiredService<ITenantStorageScopeBypass>().BeginBypass())
        {
            physical = await storage.GetBlobContentAsync(
                new BlobLocation(ContainerName, $"{_Tenant}/1.txt"),
                AbortToken
            );
        }

        // then
        physical.Should().Be("scoped");
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


    private ServiceProvider _Build(string tenantId)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddSingleton<ICurrentTenant>(new FixedCurrentTenant(tenantId));
        builder.Services.AddHeadlessBlobs(blobs =>
            blobs.UseFileSystem(options => options.BaseDirectoryPath = _baseDirectoryPath)
        );
        builder.AddHeadlessTenancy(tenancy => tenancy.Blobs(b => b.ScopeByTenant()));

        var provider = builder.Services.BuildServiceProvider();
        _providers.Add(provider);

        return provider;
    }

    /// <summary>A tenant fixed for the provider's lifetime, so the harness needs no ambient scope per test.</summary>
    private sealed class FixedCurrentTenant(string id) : ICurrentTenant
    {
        public bool IsAvailable => true;

        public string? Id => id;

        public string? Name => null;

        public IDisposable Change(string? id, string? name = null)
        {
            throw new NotSupportedException("The conformance tenant is fixed.");
        }
    }
}
