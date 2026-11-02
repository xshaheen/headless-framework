// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class EncryptionKeysProvisionerTests : TestBase
{
    private readonly InMemoryDataKeyStore _store = new();

    [Fact]
    public async Task should_fail_verify_naming_the_command_until_the_host_keys_are_provisioned()
    {
        using var host = new EncryptionTestHost(_store);
        var provisioner = _Provisioner(host);

        var verify = () => provisioner.VerifyAsync(AbortToken);

        await verify
            .Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage("*resources provision -k EncryptionKeys*");

        await provisioner.ProvisionAsync(AbortToken);
        await provisioner.ProvisionAsync(AbortToken);

        await verify.Should().NotThrowAsync();
        _store.Snapshot().Should().HaveCount(2).And.OnlyContain(k => k.Scope == FieldContext.HostScope);
        (await provisioner.GetStatusAsync(AbortToken))
            .Select(pair => $"{pair.Key}={pair.Value}")
            .Should()
            .Equal("data=version 1", "lookup=version 1");
    }

    [Fact]
    public async Task should_never_purge_or_delete_keys()
    {
        using var host = new EncryptionTestHost(_store);
        var provisioner = _Provisioner(host);

        provisioner.CanPurge.Should().BeFalse();
        provisioner.CanDelete.Should().BeFalse();
        await FluentActions
            .Awaiting(() => provisioner.PurgeAsync(AbortToken))
            .Should()
            .ThrowAsync<NotSupportedException>();
        await FluentActions
            .Awaiting(() => provisioner.DeleteAsync(AbortToken))
            .Should()
            .ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task should_hold_readiness_until_the_host_key_exists_and_never_create_it()
    {
        using var host = new EncryptionTestHost(_store);
        var warmup = host
            .Services.GetServices<IWarmup>()
            .Single(w => string.Equals(w.Name, "KeyEncryptionKey", StringComparison.Ordinal));

        await FluentActions
            .Awaiting(() => warmup.WarmUpAsync(AbortToken))
            .Should()
            .ThrowAsync<InvalidOperationException>();
        _store.Snapshot().Should().BeEmpty();

        await _Provisioner(host).ProvisionAsync(AbortToken);

        await FluentActions.Awaiting(() => warmup.WarmUpAsync(AbortToken)).Should().NotThrowAsync();
        warmup.GatesReadiness.Should().BeTrue();
    }

    private static IResourceProvisioner _Provisioner(EncryptionTestHost host) =>
        host
            .Services.GetServices<IResourceProvisioner>()
            .Single(p => string.Equals(p.Kind, "EncryptionKeys", StringComparison.Ordinal));
}
