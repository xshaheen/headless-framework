// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Blobs;
using Framework.Blobs.SshNet;
using Microsoft.Extensions.Options;
using Tests.TestSetup;

namespace Tests;

[Collection<SshBlobTestFixture>]
public sealed class SshBlobStorageTests(SshBlobTestFixture fixture) : BlobStorageTestsBase
{
    protected override IBlobStorage GetStorage()
    {
        var options = new SshBlobStorageOptions { ConnectionString = fixture.GetConnectionString() };
        var optionsWrapper = new OptionsWrapper<SshBlobStorageOptions>(options);

        return new SshBlobStorage(optionsWrapper, new CrossOsNamingNormalizer());
    }

    [Fact]
    public void can_create_ssh_file_storage_without_Connection_string_password()
    {
        // given
        var options = new SshBlobStorageOptions { ConnectionString = "sftp://framework@localhost:2222" };
        var optionsWrapper = new OptionsWrapper<SshBlobStorageOptions>(options);

        // when
        using var storage = new SshBlobStorage(optionsWrapper, new CrossOsNamingNormalizer());
    }

    [Fact]
    public void can_create_ssh_file_storage_without_proxy_password()
    {
        // given
        var options = new SshBlobStorageOptions
        {
            ConnectionString = "sftp://username@host",
            Proxy = "proxy://username@host",
        };
        var optionsWrapper = new OptionsWrapper<SshBlobStorageOptions>(options);

        // when
        using var storage = new SshBlobStorage(optionsWrapper, new CrossOsNamingNormalizer());
    }

    [Fact]
    public async Task will_not_return_directory_in_get_page()
    {
        using var storage = GetStorage();

        await ResetAsync(storage);

        var container = Container;
        var containerName = ContainerName;

        var result = await storage.GetPagedListAsync(container, cancellationToken: AbortToken);
        result.HasMore.Should().BeFalse();
        result.Blobs.Should().BeEmpty();
        (await result.NextPageAsync(AbortToken)).Should().BeFalse();
        result.HasMore.Should().BeFalse();
        result.Blobs.Should().BeEmpty();

        const string directory = "EmptyDirectory";
        await storage.CreateContainerAsync([..container, directory], AbortToken);

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
        (await storage.ExistsAsync([..container, directory], AbortToken))
            .Should()
            .BeFalse();
        (await storage.GetBlobInfoAsync(container, directory, AbortToken)).Should().BeNull();
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

    [Fact(Skip = "Doesn't work well with SFTP")]
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
    public override Task will_respect_stream_offset()
    {
        return base.will_respect_stream_offset();
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

    #region Path Traversal Security Tests

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\..\\etc\\passwd")]
    [InlineData("subdir/../../../etc/passwd")]
    public async Task should_throw_when_blob_name_has_path_traversal(string blobName)
    {
        using var storage = GetStorage();

        // ReSharper disable once AccessToDisposedClosure
        var act = FluentActions.Awaiting(() => storage.ExistsAsync(Container, blobName, AbortToken).AsTask());

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(blobName));
    }

    [Fact]
    public async Task should_throw_when_container_has_path_traversal()
    {
        using var storage = GetStorage();
        var maliciousContainer = new[] { "uploads", "..", "..", "etc" };

        var act = FluentActions.Awaiting(() => storage.ExistsAsync(maliciousContainer, "passwd", AbortToken).AsTask());

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("container");
    }

    [Fact]
    public async Task should_throw_when_upload_blob_has_path_traversal()
    {
        // given
        using var storage = GetStorage();
        await using var stream = new MemoryStream("test"u8.ToArray());

        // when
        var act = FluentActions.Awaiting(
            () =>
                storage
                    .UploadAsync(Container, "../../.ssh/authorized_keys", stream, cancellationToken: AbortToken)
                    .AsTask()
        );

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("blobName");
    }

    [Fact]
    public async Task should_throw_when_download_blob_has_path_traversal()
    {
        // given
        using var storage = GetStorage();

        // when
        var act = FluentActions.Awaiting(
            () => storage.DownloadAsync(Container, "../../../etc/passwd", AbortToken).AsTask()
        );

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("blobName");
    }

    [Fact]
    public async Task should_throw_when_delete_blob_has_path_traversal()
    {
        // given
        using var storage = GetStorage();

        // when
        var act = FluentActions.Awaiting(
            () => storage.DeleteAsync(Container, "../../../../important/file.txt", AbortToken).AsTask()
        );

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("blobName");
    }

    [Fact]
    public async Task should_throw_when_rename_source_blob_has_path_traversal()
    {
        // given
        using var storage = GetStorage();

        // when
        var act = FluentActions.Awaiting(
            () => storage.RenameAsync(Container, "../secret", Container, "newname", AbortToken).AsTask()
        );

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("blobName");
    }

    [Fact]
    public async Task should_throw_when_copy_source_blob_has_path_traversal()
    {
        // given
        using var storage = GetStorage();

        // when
        var act = FluentActions.Awaiting(
            () => storage.CopyAsync(Container, "../secret", Container, "newname", AbortToken).AsTask()
        );

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("blobName");
    }

    [Fact]
    public async Task should_throw_when_blob_name_has_control_characters()
    {
        // given
        using var storage = GetStorage();

        // when
        // ReSharper disable once AccessToDisposedClosure
        // ReSharper disable once VariableLengthStringHexEscapeSequence
        // ReSharper disable once CanSimplifyStringEscapeSequence
        var act = FluentActions.Awaiting(() => storage.ExistsAsync(Container, "file\x00name.txt", AbortToken).AsTask());

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("blobName");
    }

    [Theory]
    [InlineData("/etc/passwd")]
    public async Task should_throw_when_blob_name_is_absolute_path(string blobName)
    {
        // given
        using var storage = GetStorage();

        // when
        // ReSharper disable once AccessToDisposedClosure
        var act = FluentActions.Awaiting(() => storage.ExistsAsync(Container, blobName, AbortToken).AsTask());

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(blobName));
    }

    #endregion
}
