// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Headless.Abstractions;
using Headless.Blobs;
using Headless.Blobs.Aws;
using Headless.Blobs.CloudflareR2;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Cross-provider conformance run against a real Cloudflare R2 account. There is no R2 emulator, so this suite is
/// credentials-gated: when the R2_* environment variables are absent (local runs, forks), every scenario that touches
/// the storage skips instead of failing. The configured token must allow bucket creation for the full suite to run.
/// </summary>
/// <remarks>
/// R2 deliberately exposes no <see cref="IBlobContainerManager"/> capability (its object-scoped tokens cannot manage
/// buckets), so <see cref="BlobStorageTestsBase.SupportsContainerManagement"/> is <see langword="false"/> and
/// <see cref="BlobStorageTestsBase.GetContainerManager"/> returns <see langword="null"/>. Conformance buckets are
/// provisioned out of band by this leaf (raw <c>PutBucket</c>), mirroring how R2 buckets are created in production.
/// </remarks>
public sealed class CloudflareR2BlobStorageTests : BlobStorageTestsBase
{
    // R2 has no bucket-lifecycle capability; the data plane never auto-creates a bucket and no manager is registered.
    protected override bool SupportsContainerManagement => false;

    // Mixed-case container that the R2 normalizer lowercases onto ContainerName's backing bucket ("storage").
    protected override string NormalizationSensitiveContainer => "STORAGE";

    protected override IBlobStorage GetStorage()
    {
        var (accountId, accessKey, secretKey) = _RequireR2Credentials();

#pragma warning disable CA2000 // Disposed by AwsBlobStorage on its own DisposeAsync.
        var client = _CreateR2Client(accountId, accessKey, secretKey);
#pragma warning restore CA2000

        return _CreateR2Storage(client);
    }

    #region Provider-specific scenarios

    [Fact]
    public async Task presigned_download_url_targets_the_r2_endpoint()
    {
        // Presigning is a local SigV4 operation needing no real credentials or network, so this always runs (even
        // without R2 creds) and keeps the module from reporting zero tests.
        var config = new AmazonS3Config
        {
            ServiceURL = "https://testacc.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
        };

#pragma warning disable CA2000 // Disposed via the storage.
        var client = new AmazonS3Client(new BasicAWSCredentials("ak", "sk"), config);
#pragma warning restore CA2000

        await using var storage = new AwsBlobStorage(
            client,
            new MimeTypeProvider(),
            TimeProvider.System,
            Options.Create(new AwsBlobStorageOptions()),
            new R2BlobNamingNormalizer()
        );

        var url = await storage.GetPresignedDownloadUrlAsync(
            new BlobLocation("bucket", "file.txt"),
            TimeSpan.FromMinutes(5),
            AbortToken
        );

        url.Host.Should().Be("testacc.r2.cloudflarestorage.com");
        url.AbsolutePath.Should().Contain("bucket");
    }

    [Fact]
    public async Task can_round_trip_via_presigned_download_url()
    {
        var (accountId, accessKey, secretKey) = _RequireR2Credentials();

#pragma warning disable CA2000 // Disposed by the storage.
        var client = _CreateR2Client(accountId, accessKey, secretKey);
#pragma warning restore CA2000
        await using var storage = _CreateR2Storage(client);
        var presigned = (IPresignedUrlBlobStorage)storage;

        var container = $"presign-{Guid.NewGuid():N}";
        var location = new BlobLocation(container, "file.txt");
        var content = "presigned-content"u8.ToArray();

        await _EnsureBucketAsync(client, container, AbortToken);

        await using (var stream = new MemoryStream(content))
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
        var (accountId, accessKey, secretKey) = _RequireR2Credentials();

#pragma warning disable CA2000 // Disposed by the storage.
        var client = _CreateR2Client(accountId, accessKey, secretKey);
#pragma warning restore CA2000
        await using var storage = _CreateR2Storage(client);
        var presigned = (IPresignedUrlBlobStorage)storage;

        var container = $"presign-{Guid.NewGuid():N}";
        var location = new BlobLocation(container, "file.txt");
        var content = "presigned-upload"u8.ToArray();

        // The presigned PUT goes straight to R2 and does not create the bucket; provision it first (the conformance
        // token must allow bucket creation).
        await _EnsureBucketAsync(client, container, AbortToken);

        var uploadUrl = await presigned.GetPresignedUploadUrlAsync(location, TimeSpan.FromMinutes(5), AbortToken);

        using (var http = new HttpClient())
        using (var body = new ByteArrayContent(content))
        {
            var response = await http.PutAsync(uploadUrl, body, AbortToken);
            response.EnsureSuccessStatusCode();
        }

        await using var readBack = await storage.OpenReadStreamAsync(location, AbortToken);
        readBack.Should().NotBeNull();

        await using var buffer = new MemoryStream();
        await readBack!.Stream.CopyToAsync(buffer, AbortToken);
        buffer.ToArray().Should().Equal(content);
    }

    [Fact]
    public async Task presigned_download_url_is_denied_after_expiry()
    {
        var (accountId, accessKey, secretKey) = _RequireR2Credentials();

#pragma warning disable CA2000 // Disposed by the storage.
        var client = _CreateR2Client(accountId, accessKey, secretKey);
#pragma warning restore CA2000
        await using var storage = _CreateR2Storage(client);
        var presigned = (IPresignedUrlBlobStorage)storage;

        var container = $"presign-{Guid.NewGuid():N}";
        var location = new BlobLocation(container, "file.txt");

        await _EnsureBucketAsync(client, container, AbortToken);

        await using (var stream = new MemoryStream("expiring"u8.ToArray()))
        {
            await storage.UploadAsync(location, stream, cancellationToken: AbortToken);
        }

        var url = await presigned.GetPresignedDownloadUrlAsync(location, TimeSpan.FromSeconds(2), AbortToken);

        await Task.Delay(TimeSpan.FromSeconds(4), AbortToken);

        using var http = new HttpClient();
        var response = await http.GetAsync(url, AbortToken);

        response.IsSuccessStatusCode.Should().BeFalse();
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
    public override Task move_relocates_blob_and_metadata()
    {
        return base.move_relocates_blob_and_metadata();
    }

    [Fact]
    public override Task list_metadata_is_opt_in()
    {
        return base.list_metadata_is_opt_in();
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

    #region Helpers

    private static (string accountId, string accessKey, string secretKey) _RequireR2Credentials()
    {
        var accountId = Environment.GetEnvironmentVariable("R2_ACCOUNT_ID");
        var accessKey = Environment.GetEnvironmentVariable("R2_ACCESS_KEY_ID");
        var secretKey = Environment.GetEnvironmentVariable("R2_SECRET_ACCESS_KEY");

        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(accountId)
                || string.IsNullOrWhiteSpace(accessKey)
                || string.IsNullOrWhiteSpace(secretKey),
            "R2 credentials are not configured (R2_ACCOUNT_ID, R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY)."
        );

        return (accountId!, accessKey!, secretKey!);
    }

    // Build the client through the same factory the production setup uses, so the conformance suite can never drift
    // from the real R2 client configuration.
    private static IAmazonS3 _CreateR2Client(string accountId, string accessKey, string secretKey)
    {
        return R2ClientFactory.Create(
            new R2BlobStorageOptions
            {
                AccountId = accountId,
                AccessKeyId = accessKey,
                SecretAccessKey = secretKey,
            }
        );
    }

    private static AwsBlobStorage _CreateR2Storage(IAmazonS3 client)
    {
        return new(
            client,
            new MimeTypeProvider(),
            TimeProvider.System,
            // R2-forced behavior: no ACLs, no chunked encoding, payload signing disabled.
            Options.Create(
                new AwsBlobStorageOptions
                {
                    CannedAcl = null,
                    UseChunkEncoding = false,
                    DisablePayloadSigning = true,
                }
            ),
            new R2BlobNamingNormalizer()
        );
    }

    // R2 exposes no IBlobContainerManager; conformance buckets are provisioned out of band by the leaf. The bucket
    // name must match the resolver's normalization so a later upload/presign targets the same bucket.
    private static async Task _EnsureBucketAsync(
        IAmazonS3 client,
        string container,
        CancellationToken cancellationToken
    )
    {
        var bucket = new R2BlobNamingNormalizer().NormalizeContainerName(container);

        try
        {
            await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, cancellationToken);
        }
        catch (AmazonS3Exception e)
            when (string.Equals(e.ErrorCode, "BucketAlreadyOwnedByYou", StringComparison.Ordinal))
        {
            // Already provisioned.
        }
    }

    #endregion
}
