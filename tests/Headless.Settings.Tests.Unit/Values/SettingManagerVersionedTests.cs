// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless;
using Headless.Context;
using Headless.Messaging;
using Headless.MultiTenancy;
using Headless.Settings;
using Headless.Settings.Resources;
using Headless.Testing.Tests;
using NSubstitute.ExceptionExtensions;
using Tests.Fakes;

namespace Tests.Values;

public sealed class SettingManagerVersionedTests : TestBase
{
    private readonly ISettingDefinitionManager _definitionManager = Substitute.For<ISettingDefinitionManager>();
    private readonly ISettingValueStore _store = Substitute.For<ISettingValueStore>();
    private readonly ISettingValueProviderManager _providerManager = Substitute.For<ISettingValueProviderManager>();
    private readonly IBus _bus = Substitute.For<IBus>();
    private readonly SettingManager _sut;

    public SettingManagerVersionedTests()
    {
        _definitionManager.FindAsync("App.Theme", AbortToken).Returns(new SettingDefinition("App.Theme"));

        _sut = new SettingManager(
            _definitionManager,
            _store,
            _providerManager,
            Substitute.For<ISettingEncryptionService>(),
            new DefaultSettingErrorsDescriptor(),
            Substitute.For<IHostIdentityAccessor>(),
            Substitute.For<ICurrentTenant>(),
            _bus
        );
    }

    [Fact]
    public async Task should_refuse_a_stale_save_with_version_mismatch_and_announce_nothing()
    {
        // given
        _providerManager.Providers.Returns([new FakeStoreSettingValueProvider(_store)]);
        _store
            .SetAllVersionedAsync(Arg.Any<IReadOnlyDictionary<string, string?>>(), "Store", "t1", "stale", AbortToken)
            .ThrowsAsync(new DBConcurrencyException("changed"));

        // when
        var act = () => _sut.SetVersionedAsync(_Values("dark"), "Store", "t1", "stale", cancellationToken: AbortToken);

        // then
        var exception = (await act.Should().ThrowAsync<ConflictException>()).Which;
        exception.Errors.Should().ContainSingle().Which.Code.Should().Be("setting:version_mismatch");
        await _bus.DidNotReceive()
            .PublishAsync(Arg.Any<SettingChangedMessage>(), Arg.Any<PublishOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_announce_one_change_after_a_current_save()
    {
        // given
        _providerManager.Providers.Returns([new FakeStoreSettingValueProvider(_store)]);

        // when
        await _sut.SetVersionedAsync(_Values("dark"), "Store", "t1", "current", cancellationToken: AbortToken);

        // then
        await _store
            .Received(1)
            .SetAllVersionedAsync(
                Arg.Is<IReadOnlyDictionary<string, string?>>(x => x["App.Theme"] == "dark"),
                "Store",
                "t1",
                "current",
                AbortToken
            );
        await _bus.Received(1)
            .PublishAsync(
                Arg.Is<SettingChangedMessage>(x => x.SettingNames.SequenceEqual(new[] { "App.Theme" })),
                Arg.Any<PublishOptions>(),
                AbortToken
            );
    }

    [Fact]
    public async Task should_refuse_a_versioned_save_when_two_providers_share_the_name()
    {
        // given — a version covers one store, so a name shared by two writable providers has no single version
        _providerManager.Providers.Returns([
            new FakeStoreSettingValueProvider(_store),
            new FakeStoreSettingValueProvider(_store),
        ]);

        // when
        var act = () => _sut.SetVersionedAsync(_Values("dark"), "Store", "t1", "v1", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowExactlyAsync<NotSupportedException>();
        await _store
            .DidNotReceive()
            .SetAllVersionedAsync(
                Arg.Any<IReadOnlyDictionary<string, string?>>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_refuse_a_versioned_read_through_a_provider_that_cannot_version()
    {
        // given
        _providerManager.Providers.Returns([new FakeSettingValueProvider { Name = "Store" }]);

        // when
        var act = () => _sut.GetVersionedAsync(["App.Theme"], "Store", "t1", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowExactlyAsync<NotSupportedException>();
    }

    [Fact]
    public async Task should_reject_an_empty_versioned_save()
    {
        // when
        var act = () =>
            _sut.SetVersionedAsync(
                new Dictionary<string, string?>(StringComparer.Ordinal),
                "Store",
                "t1",
                "v1",
                cancellationToken: AbortToken
            );

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static Dictionary<string, string?> _Values(string? theme)
    {
        return new Dictionary<string, string?>(StringComparer.Ordinal) { ["App.Theme"] = theme };
    }
}
