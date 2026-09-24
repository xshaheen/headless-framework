// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests;

/// <summary>
/// Runs the cross-provider conformance suite against MinIO through the public <c>UseS3Compatible</c> registration, so
/// the S3-compatible client and storage defaults are exercised end to end rather than hand-built.
/// </summary>
[Collection<MinioFixture>]
public sealed class S3CompatibleMinioBlobStorageTests(MinioFixture fixture) : BlobStorageTestsBase
{
    private readonly List<ServiceProvider> _providers = [];

    private ServiceProvider _BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddSingleton<IMimeTypeProvider, MimeTypeProvider>();
        services.AddHeadlessBlobs(blobs =>
            blobs.UseS3Compatible(
                fixture.Container.GetConnectionString(),
                fixture.Container.GetAccessKey(),
                fixture.Container.GetSecretKey(),
                options => options.AllowInsecureHttp = true
            )
        );

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        return provider;
    }

    // Each call builds its own container so the conformance suite's per-call dispose never hits a shared singleton.
    protected override IBlobStorage GetStorage()
    {
        return _BuildProvider().GetRequiredService<IBlobStorage>();
    }

    protected override IBlobContainerManager GetContainerManager()
    {
        return _BuildProvider().GetRequiredService<IBlobContainerManager>();
    }

    // The AWS normalizer lowercases bucket names, so this maps onto ContainerName's backing bucket ("storage").
    protected override string NormalizationSensitiveContainer => "STORAGE";

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        _providers.Clear();

        await base.DisposeAsyncCore();
    }

    #region Provider-specific scenarios

    [Fact]
    public async Task should_upload_download_presign_and_delete_when_registered_through_use_s3_compatible()
    {
        // given
        await using var storage = GetStorage();
        var container = $"s3c-{Guid.NewGuid():N}";
        var location = new BlobLocation(container, "docs/report.txt");
        var content = "s3-compatible-content"u8.ToArray();
        await GetContainerManager().EnsureContainerAsync(container, AbortToken);

        // when
        await using (var stream = new MemoryStream(content))
        {
            await storage.UploadAsync(location, stream, cancellationToken: AbortToken);
        }

        byte[] readBytes;

        await using (var readBack = await storage.OpenReadStreamAsync(location, AbortToken))
        await using (var buffer = new MemoryStream())
        {
            await readBack!.Stream.CopyToAsync(buffer, AbortToken);
            readBytes = buffer.ToArray();
        }

        var url = await ((IPresignedUrlBlobStorage)storage).GetPresignedDownloadUrlAsync(
            location,
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        using var http = new HttpClient();
        var downloaded = await http.GetByteArrayAsync(url, AbortToken);

        var deleted = await storage.DeleteAsync(location, AbortToken);

        // then
        readBytes.Should().Equal(content);
        url.Scheme.Should().Be(Uri.UriSchemeHttp);
        downloaded.Should().Equal(content);
        deleted.Should().BeTrue();
        (await storage.ExistsAsync(location, AbortToken)).Should().BeFalse();
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

    #region Token paging

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

    #region Path traversal & construction security

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
    [InlineData("../secret/")]
    [InlineData("..\\secret\\")]
    [InlineData("a/../../b")]
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
