// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Blobs;
using Headless.DataProtection;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class DataProtectionKeysProvisionerTests : TestBase
{
    [Fact]
    public async Task should_create_the_container_and_one_key_and_keep_it_when_provision_runs_again()
    {
        // given
        var stored = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        var manager = Substitute.For<IBlobContainerManager>();
        await using var provider = _BuildProvider(InMemoryKeyStorage.Fake(stored), manager);
        var sut = await _GetProvisionerAsync(provider);

        // when: a release re-runs the deploy step.
        await sut.ProvisionAsync(AbortToken);
        var firstKey = stored.Keys.Single();
        await sut.ProvisionAsync(AbortToken);

        // then: the container is ensured each run, and the first key is created exactly once.
        await manager.Received(2).EnsureContainerAsync("DataProtection", Arg.Any<CancellationToken>());
        stored.Keys.Should().Equal(firstKey);
    }

    [Fact]
    public async Task should_create_the_first_key_without_a_manager_when_the_container_is_pre_provisioned()
    {
        // given
        var stored = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        await using var provider = _BuildProvider(InMemoryKeyStorage.Fake(stored), manager: null);
        var sut = await _GetProvisionerAsync(provider);

        // when
        await sut.ProvisionAsync(AbortToken);

        // then
        stored.Should().ContainSingle();
        var status = await sut.GetStatusAsync(AbortToken);
        status.Should().Equal(new("container", "pre-provisioned"), new KeyValuePair<string, string>("keys", "1"));
    }

    [Fact]
    public async Task should_fail_verify_naming_resources_provision_until_provision_runs()
    {
        // given: an existing but empty container.
        var manager = Substitute.For<IBlobContainerManager>();
        manager
            .ContainerExistsAsync("DataProtection", Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        await using var provider = _BuildProvider(InMemoryKeyStorage.Fake(), manager);
        var sut = await _GetProvisionerAsync(provider);

        // when
        var before = async () => await sut.VerifyAsync(AbortToken);

        // then
        await before
            .Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage("*holds no key*resources provision -k DataProtectionKeys*");

        // when
        await sut.ProvisionAsync(AbortToken);

        // then
        await sut.Invoking(p => p.VerifyAsync(AbortToken)).Should().NotThrowAsync();
        (await sut.GetStatusAsync(AbortToken))
            .Should()
            .Equal(new("container", "exists"), new KeyValuePair<string, string>("keys", "1"));
    }

    [Fact]
    public async Task should_fail_verify_and_report_missing_without_listing_when_the_container_does_not_exist()
    {
        // given
        var storage = InMemoryKeyStorage.Fake();
        var manager = Substitute.For<IBlobContainerManager>();
        manager
            .ContainerExistsAsync("DataProtection", Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));
        await using var provider = _BuildProvider(storage, manager);
        var sut = await _GetProvisionerAsync(provider);

        // when
        var act = async () => await sut.VerifyAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage("*'DataProtection' does not exist*resources provision -k DataProtectionKeys*");
        (await sut.GetStatusAsync(AbortToken))
            .Should()
            .Equal(new("container", "missing"), new KeyValuePair<string, string>("keys", "0"));
        await storage.DidNotReceiveWithAnyArgs().ListAsync(default!, CancellationToken.None);
    }

    [Fact]
    public async Task should_refuse_purge_and_delete_because_they_would_orphan_every_protected_payload()
    {
        // given
        await using var provider = _BuildProvider(InMemoryKeyStorage.Fake(), Substitute.For<IBlobContainerManager>());
        var sut = await _GetProvisionerAsync(provider);

        // then
        sut.CanPurge.Should().BeFalse();
        sut.CanDelete.Should().BeFalse();
        await sut.Invoking(p => p.PurgeAsync(AbortToken)).Should().ThrowAsync<NotSupportedException>();
        await sut.Invoking(p => p.DeleteAsync(AbortToken)).Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task should_offer_no_provisioner_when_another_repository_replaced_the_blob_key_ring()
    {
        // given: a later PersistKeysTo* call owns the key ring.
        var services = new ServiceCollection();
        services
            .AddDataProtection()
            .PersistKeysToBlobStorage(InMemoryKeyStorage.Fake(), Substitute.For<IBlobContainerManager>());
        services.Configure<KeyManagementOptions>(options => options.XmlRepository = Substitute.For<IXmlRepository>());
        await using var provider = services.BuildServiceProvider();

        // when
        var provisioners = await provider
            .GetServices<IResourceProvisionerSource>()
            .OfType<DataProtectionKeysProvisionerSource>()
            .Single()
            .GetProvisionersAsync(AbortToken);

        // then
        provisioners.Should().BeEmpty();
    }

    #region Helper Methods

    private static ServiceProvider _BuildProvider(IBlobStorage storage, IBlobContainerManager? manager)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddDataProtection();

        if (manager is null)
        {
            builder.PersistKeysToBlobStorage(storage, provisioning: BlobContainerProvisioning.PreProvisioned);
        }
        else
        {
            builder.PersistKeysToBlobStorage(storage, manager);
        }

        return services.BuildServiceProvider();
    }

    private async Task<IResourceProvisioner> _GetProvisionerAsync(IServiceProvider provider)
    {
        var provisioners = await provider
            .GetServices<IResourceProvisionerSource>()
            .OfType<DataProtectionKeysProvisionerSource>()
            .Single()
            .GetProvisionersAsync(AbortToken);

        return provisioners.Should().ContainSingle().Which;
    }

    #endregion
}
