// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Runtime;
using Amazon.S3;
using Headless.Abstractions;
using Headless.Blobs;
using Headless.Blobs.Aws;
using Headless.Blobs.CloudflareR2;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Cross-provider conformance run against a real Cloudflare R2 account. There is no R2 emulator, so this suite
/// is credentials-gated: when the R2_* environment variables are absent (local runs, forks), every scenario
/// skips instead of failing. The configured token must allow bucket creation for the full suite to run.
/// </summary>
public sealed class CloudflareR2BlobStorageTests : BlobStorageTestsBase
{
    protected override IBlobStorage GetStorage()
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

        // Build the client through the same factory the production setup uses, so the conformance suite can
        // never drift from the real R2 client configuration (the whole point of the SDK-bump gate).
#pragma warning disable CA2000 // Disposed by AwsBlobStorage / the test host.
        var client = R2ClientFactory.Create(
            new R2BlobStorageOptions
            {
                AccountId = accountId!,
                AccessKeyId = accessKey!,
                SecretAccessKey = secretKey!,
            }
        );
#pragma warning restore CA2000

        // Conformance exercises the full lifecycle, so it needs bucket creation; the test token must allow it.
        var options = new OptionsWrapper<AwsBlobStorageOptions>(
            new AwsBlobStorageOptions
            {
                CannedAcl = null,
                UseChunkEncoding = false,
                DisablePayloadSigning = true,
                AutoCreateContainer = true,
            }
        );

        return new AwsBlobStorage(
            client,
            new MimeTypeProvider(),
            new Clock(TimeProvider.System),
            options,
            new R2BlobNamingNormalizer()
        );
    }

    [Fact]
    public async Task presigned_download_url_targets_the_r2_endpoint()
    {
        // Presigning is a local SigV4 operation needing no real credentials or network, so this always runs
        // (even without R2 creds) and keeps the module from reporting zero tests.
        var config = new AmazonS3Config
        {
            ServiceURL = "https://testacc.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
        };

#pragma warning disable CA2000 // Disposed via the storage.
        var client = new AmazonS3Client(new BasicAWSCredentials("ak", "sk"), config);
#pragma warning restore CA2000

        var options = new OptionsWrapper<AwsBlobStorageOptions>(
            new AwsBlobStorageOptions { AutoCreateContainer = false }
        );
        await using var storage = new AwsBlobStorage(
            client,
            new MimeTypeProvider(),
            new Clock(TimeProvider.System),
            options,
            new R2BlobNamingNormalizer()
        );

        var url = await storage.GetPresignedDownloadUrlAsync(["bucket"], "file.txt", TimeSpan.FromMinutes(5));

        url.Host.Should().Be("testacc.r2.cloudflarestorage.com");
        url.AbsolutePath.Should().Contain("bucket");
    }

    [Fact]
    public async Task can_round_trip_via_presigned_download_url()
    {
        var storage = (IPresignedUrlBlobStorage)GetStorage();
        var container = new[] { $"presign-{Guid.NewGuid():N}" };
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
        var container = new[] { $"presign-{Guid.NewGuid():N}" };
        var content = "presigned-upload"u8.ToArray();

        // The presigned PUT goes straight to R2 and does not create the bucket; create it first (the
        // conformance token allows bucket creation).
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
    public async Task presigned_download_url_is_denied_after_expiry()
    {
        var storage = (IPresignedUrlBlobStorage)GetStorage();
        var container = new[] { $"presign-{Guid.NewGuid():N}" };

        using (var stream = new MemoryStream("expiring"u8.ToArray()))
        {
            await ((IBlobStorage)storage).UploadAsync(container, "file.txt", stream, cancellationToken: AbortToken);
        }

        var url = await storage.GetPresignedDownloadUrlAsync(
            container,
            "file.txt",
            TimeSpan.FromSeconds(2),
            AbortToken
        );

        await Task.Delay(TimeSpan.FromSeconds(4), AbortToken);

        using var http = new HttpClient();
        var response = await http.GetAsync(url, AbortToken);

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public override Task can_get_empty_file_list_on_missing_directory() =>
        base.can_get_empty_file_list_on_missing_directory();

    [Fact]
    public override Task can_get_file_list_for_single_folder() => base.can_get_file_list_for_single_folder();

    [Fact]
    public override Task can_get_file_list_for_single_file() => base.can_get_file_list_for_single_file();

    [Fact]
    public override Task can_get_paged_file_list_for_single_folder() =>
        base.can_get_paged_file_list_for_single_folder();

    [Fact]
    public override Task can_get_file_info() => base.can_get_file_info();

    [Fact]
    public override Task can_get_non_existent_file_info() => base.can_get_non_existent_file_info();

    [Fact]
    public override Task can_manage_files() => base.can_manage_files();

    [Fact]
    public override Task can_rename_files() => base.can_rename_files();

    [Fact]
    public override Task can_concurrently_manage_files() => base.can_concurrently_manage_files();

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
    public override Task can_round_trip_seekable_stream() => base.can_round_trip_seekable_stream();

    [Fact]
    public override Task will_reset_stream_position() => base.will_reset_stream_position();

    [Fact]
    public override Task can_save_over_existing_stored_content() => base.can_save_over_existing_stored_content();

    [Fact]
    public override Task can_call_delete_with_empty_container() => base.can_call_delete_with_empty_container();

    [Fact]
    public override Task can_call_bulk_Delete_with_empty_container() =>
        base.can_call_bulk_Delete_with_empty_container();

    [Fact]
    public override Task can_call_delete_all_async_with_empty_container() =>
        base.can_call_delete_all_async_with_empty_container();

    [Fact]
    public override Task can_call_copy_with_empty_container() => base.can_call_copy_with_empty_container();

    [Fact]
    public override Task can_call_rename_with_empty_container() => base.can_call_rename_with_empty_container();

    [Fact]
    public override Task can_call_exists_with_empty_container() => base.can_call_exists_with_empty_container();

    [Fact]
    public override Task can_call_download_with_empty_container() => base.can_call_download_with_empty_container();

    [Fact]
    public override Task can_call_get_blob_info_with_empty_container() =>
        base.can_call_get_blob_info_with_empty_container();

    [Fact]
    public override Task can_call_get_paged_list_with_empty_container() =>
        base.can_call_get_paged_list_with_empty_container();

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\..\\etc\\passwd")]
    [InlineData("subdir/../../../etc/passwd")]
    public override Task should_throw_when_blob_name_has_path_traversal(string blobName) =>
        base.should_throw_when_blob_name_has_path_traversal(blobName);

    [Fact]
    public override Task should_throw_when_container_has_path_traversal() =>
        base.should_throw_when_container_has_path_traversal();

    [Fact]
    public override Task should_throw_when_upload_blob_has_path_traversal() =>
        base.should_throw_when_upload_blob_has_path_traversal();

    [Fact]
    public override Task should_throw_when_download_blob_has_path_traversal() =>
        base.should_throw_when_download_blob_has_path_traversal();

    [Fact]
    public override Task should_throw_when_delete_blob_has_path_traversal() =>
        base.should_throw_when_delete_blob_has_path_traversal();

    [Fact]
    public override Task should_throw_when_rename_source_blob_has_path_traversal() =>
        base.should_throw_when_rename_source_blob_has_path_traversal();

    [Fact]
    public override Task should_throw_when_copy_source_blob_has_path_traversal() =>
        base.should_throw_when_copy_source_blob_has_path_traversal();

    [Fact]
    public override Task should_throw_when_blob_name_has_control_characters() =>
        base.should_throw_when_blob_name_has_control_characters();

    [Theory]
    [InlineData("/etc/passwd")]
    public override Task should_throw_when_blob_name_is_absolute_path(string blobName) =>
        base.should_throw_when_blob_name_is_absolute_path(blobName);
}
