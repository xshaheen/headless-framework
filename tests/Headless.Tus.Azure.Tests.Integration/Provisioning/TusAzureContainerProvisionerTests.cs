// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Hosting;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests.Provisioning;

[Collection<TusAzureFixture>]
public sealed class TusAzureContainerProvisionerTests(TusAzureFixture fixture) : TestBase
{
    private const string _BlobPrefix = "uploads/";

    private readonly BlobServiceClient _blobServiceClient = new(
        fixture.Container.GetConnectionString(),
        new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
    );

    // A container per test: delete and purge assertions must not see another test's blobs.
    private readonly TusAzureStoreOptions _options = new()
    {
        ContainerName = $"tusprov{Guid.NewGuid():N}",
        BlobPrefix = _BlobPrefix,
    };

    private BlobContainerClient _Container => _blobServiceClient.GetBlobContainerClient(_options.ContainerName);

    private TusAzureContainerProvisioner _CreateProvisioner() => new(_blobServiceClient, _options);

    [Fact]
    public async Task should_create_missing_container_when_provisioning()
    {
        // given
        _options.ContainerPublicAccessType = PublicAccessType.Blob;
        var provisioner = _CreateProvisioner();

        // when
        await provisioner.ProvisionAsync(AbortToken);
        await provisioner.ProvisionAsync(AbortToken);

        // then
        (await _Container.ExistsAsync(AbortToken))
            .Value.Should()
            .BeTrue();
        var properties = await _Container.GetPropertiesAsync(cancellationToken: AbortToken);
        properties.Value.PublicAccess.Should().Be(PublicAccessType.Blob);
    }

    [Fact]
    public async Task should_fail_verification_when_container_is_missing()
    {
        // given
        var provisioner = _CreateProvisioner();

        // when
        var act = () => provisioner.VerifyAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage($"*'{_options.ContainerName}'*resources provision -k AzureBlob*");
    }

    [Fact]
    public async Task should_pass_verification_when_container_exists()
    {
        // given
        var provisioner = _CreateProvisioner();
        await provisioner.ProvisionAsync(AbortToken);

        // when
        var act = () => provisioner.VerifyAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_report_container_state_in_status()
    {
        // given
        var provisioner = _CreateProvisioner();

        // when
        var before = await provisioner.GetStatusAsync(AbortToken);
        await provisioner.ProvisionAsync(AbortToken);
        var after = await provisioner.GetStatusAsync(AbortToken);

        // then
        before.Should().Contain(new KeyValuePair<string, string>("container", "missing"));
        after.Should().Contain(new KeyValuePair<string, string>("container", "exists"));
        after.Should().Contain(new KeyValuePair<string, string>("prefix", _BlobPrefix));
    }

    [Fact]
    public async Task should_purge_only_uploads_under_prefix()
    {
        // given
        var provisioner = _CreateProvisioner();
        await provisioner.ProvisionAsync(AbortToken);
        await _UploadAsync(_BlobPrefix + "upload-1");
        await _UploadAsync("other/kept");

        // when
        await provisioner.PurgeAsync(AbortToken);

        // then
        (await _Container.ExistsAsync(AbortToken))
            .Value.Should()
            .BeTrue();
        (await _BlobNamesAsync()).Should().Equal("other/kept");
    }

    [Fact]
    public async Task should_purge_only_uploads_when_prefix_has_no_trailing_slash()
    {
        // given
        _options.BlobPrefix = "uploads";
        var provisioner = _CreateProvisioner();
        await provisioner.ProvisionAsync(AbortToken);
        await _UploadAsync("uploads/upload-1");
        await _UploadAsync("uploads-other/kept");

        // when
        await provisioner.PurgeAsync(AbortToken);
        var status = await provisioner.GetStatusAsync(AbortToken);

        // then
        (await _BlobNamesAsync())
            .Should()
            .Equal("uploads-other/kept");
        status.Should().Contain(new KeyValuePair<string, string>("prefix", "uploads/"));
    }

    [Fact]
    public async Task should_delete_container_when_it_holds_only_uploads()
    {
        // given
        var provisioner = _CreateProvisioner();
        await provisioner.ProvisionAsync(AbortToken);
        await _UploadAsync(_BlobPrefix + "upload-1");

        // when
        await provisioner.DeleteAsync(AbortToken);
        await provisioner.DeleteAsync(AbortToken);

        // then
        (await _Container.ExistsAsync(AbortToken))
            .Value.Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_keep_container_holding_other_blobs_when_deleting()
    {
        // given
        var provisioner = _CreateProvisioner();
        await provisioner.ProvisionAsync(AbortToken);
        await _UploadAsync(_BlobPrefix + "upload-1");
        await _UploadAsync("other/kept");

        // when
        await provisioner.DeleteAsync(AbortToken);

        // then
        (await _Container.ExistsAsync(AbortToken))
            .Value.Should()
            .BeTrue();
        (await _BlobNamesAsync()).Should().Equal("other/kept");
    }

    [Fact]
    public async Task should_create_container_through_registered_provisioner()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_blobServiceClient);
        services.AddTusAzureStore(options =>
        {
            options.ContainerName = _options.ContainerName;
            options.BlobPrefix = _BlobPrefix;
        });
        await using var provider = services.BuildServiceProvider();

        // when
        await provider.ProvisionResourcesAsync(AbortToken);

        // then
        (await _Container.ExistsAsync(AbortToken))
            .Value.Should()
            .BeTrue();
    }

    private async Task _UploadAsync(string blobName)
    {
        await _Container.UploadBlobAsync(blobName, BinaryData.FromString("data"), AbortToken);
    }

    private async Task<List<string>> _BlobNamesAsync()
    {
        var names = new List<string>();

        await foreach (var blob in _Container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: null, AbortToken))
        {
            names.Add(blob.Name);
        }

        return names;
    }
}
