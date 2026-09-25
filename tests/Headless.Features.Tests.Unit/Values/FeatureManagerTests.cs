// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Features.Definitions;
using Headless.Features.Models;
using Headless.Features.Resources;
using Headless.Features.ValueProviders;
using Headless.Features.Values;
using Headless.Messaging;
using Headless.Testing.Tests;
using NSubstitute.ExceptionExtensions;

namespace Tests.Values;

public sealed class FeatureManagerTests : TestBase
{
    private readonly IFeatureDefinitionManager _definitionManager;
    private readonly IFeatureValueProviderManager _valueProviderManager;
    private readonly IFeatureValueProvider _provider;
    private readonly IBus _bus;
    private readonly IHostIdentityAccessor _hostIdentity;
    private readonly FeatureManager _sut;

    public FeatureManagerTests()
    {
        _definitionManager = Substitute.For<IFeatureDefinitionManager>();
        _valueProviderManager = Substitute.For<IFeatureValueProviderManager>();
        _provider = Substitute.For<IFeatureValueProvider>();
        _provider.Name.Returns("Provider1");
        _valueProviderManager.ValueProviders.Returns([_provider]);
        _bus = Substitute.For<IBus>();
        _hostIdentity = Substitute.For<IHostIdentityAccessor>();
        _hostIdentity.HostName.Returns("prod/orders-7d");

        _sut = new FeatureManager(
            _definitionManager,
            _valueProviderManager,
            new DefaultFeatureErrorsDescriptor(),
            _hostIdentity,
            _bus
        );
    }

    [Fact]
    public async Task should_resolve_requested_features_through_the_provider_chain_when_batch_read()
    {
        // given
        var high = Substitute.For<IFeatureValueProvider>();
        high.Name.Returns("High");
        var low = Substitute.For<IFeatureValueProvider>();
        low.Name.Returns("Low");
        _valueProviderManager.ValueProviders.Returns([high, low]);
        var fromHigh = new FeatureDefinition("FromHigh");
        var fromLow = new FeatureDefinition("FromLow");
        var unset = new FeatureDefinition("Unset");
        var notRequested = new FeatureDefinition("NotRequested");
        _definitionManager.GetFeaturesAsync(AbortToken).Returns([fromHigh, fromLow, unset, notRequested]);
        // NSubstitute auto-returns "" for an unconfigured Task<string?>; a provider without a value returns null.
        high.GetOrDefaultAsync(Arg.Any<FeatureDefinition>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        low.GetOrDefaultAsync(Arg.Any<FeatureDefinition>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        high.GetOrDefaultAsync(fromHigh, null, AbortToken).Returns("h");
        low.GetOrDefaultAsync(fromHigh, null, AbortToken).Returns("shadowed");
        low.GetOrDefaultAsync(fromLow, null, AbortToken).Returns("l");

        // when
        var values = await _sut.GetAllAsync(
            new HashSet<string>(StringComparer.Ordinal) { "FromHigh", "FromLow", "Unset", "Undefined" },
            AbortToken
        );

        // then
        values.Keys.Should().BeEquivalentTo("FromHigh", "FromLow", "Unset");
        values["FromHigh"].Should().Be(new FeatureValue("FromHigh", "h", new FeatureValueProvider("High", null)));
        values["FromLow"].Should().Be(new FeatureValue("FromLow", "l", new FeatureValueProvider("Low", null)));
        values["Unset"].Should().Be(new FeatureValue("Unset", null, null));
        await _definitionManager.DidNotReceive().FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_return_empty_without_reading_definitions_when_batch_read_is_empty()
    {
        // when
        var values = await _sut.GetAllAsync(new HashSet<string>(StringComparer.Ordinal), AbortToken);

        // then
        values.Should().BeEmpty();
        await _definitionManager.DidNotReceive().GetFeaturesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_publish_the_changed_name_and_scope_after_a_write()
    {
        // given
        const string featureName = "TestFeature";
        var definition = new FeatureDefinition(featureName);
        _definitionManager.FindAsync(featureName, AbortToken).Returns(definition);

        // when
        await _sut.SetAsync(featureName, "value", "Provider1", "key1", forceToSet: true, cancellationToken: AbortToken);

        // then
        await _bus.Received(1)
            .PublishAsync(
                Arg.Is<FeatureChangedMessage>(m =>
                    m.FeatureNames.Count == 1
                    && m.FeatureNames[0] == featureName
                    && m.ProviderName == "Provider1"
                    && m.ProviderKey == "key1"
                    && m.OriginHostName == "prod/orders-7d"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_publish_every_removed_name_when_a_provider_scope_is_deleted()
    {
        // given
        var feature1 = new FeatureDefinition("Feature1");
        var feature2 = new FeatureDefinition("Feature2");
        _definitionManager.GetFeaturesAsync(AbortToken).Returns([feature1, feature2]);
        _definitionManager.FindAsync("Feature1", AbortToken).Returns(feature1);
        _definitionManager.FindAsync("Feature2", AbortToken).Returns(feature2);
        _provider.GetOrDefaultAsync(feature1, "key1", AbortToken).Returns("a");
        _provider.GetOrDefaultAsync(feature2, "key1", AbortToken).Returns("b");

        // when
        await _sut.DeleteAsync("Provider1", "key1", AbortToken);

        // then
        await _bus.Received(1)
            .PublishAsync(
                Arg.Is<FeatureChangedMessage>(m =>
                    m.FeatureNames.Count == 2
                    && m.FeatureNames.Contains("Feature1")
                    && m.FeatureNames.Contains("Feature2")
                    && m.ProviderName == "Provider1"
                    && m.ProviderKey == "key1"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_announce_a_feature_this_scope_never_stored_when_deleting()
    {
        // given a lower provider holds Feature2, this scope holds only Feature1
        var feature1 = new FeatureDefinition("Feature1");
        var feature2 = new FeatureDefinition("Feature2");
        var fallbackProvider = Substitute.For<IFeatureValueProvider>();
        fallbackProvider.Name.Returns("Default");
        _valueProviderManager.ValueProviders.Returns([_provider, fallbackProvider]);
        _definitionManager.GetFeaturesAsync(AbortToken).Returns([feature1, feature2]);
        _definitionManager.FindAsync("Feature1", AbortToken).Returns(feature1);
        _definitionManager.FindAsync("Feature2", AbortToken).Returns(feature2);
        _provider.GetOrDefaultAsync(feature1, "key1", AbortToken).Returns("a");
        _provider.GetOrDefaultAsync(feature2, "key1", AbortToken).Returns((string?)null);
        fallbackProvider.GetOrDefaultAsync(feature2, null, AbortToken).Returns("default-b");

        // when
        await _sut.DeleteAsync("Provider1", "key1", AbortToken);

        // then Feature2 is neither cleared nor announced: nothing changed for it
        await _provider.DidNotReceive().ClearAsync(feature2, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _bus.Received(1)
            .PublishAsync(
                Arg.Is<FeatureChangedMessage>(m => m.FeatureNames.Count == 1 && m.FeatureNames[0] == "Feature1"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_publish_when_a_delete_removed_nothing()
    {
        // given
        _definitionManager.GetFeaturesAsync(AbortToken).Returns([]);

        // when
        await _sut.DeleteAsync("Provider1", "key1", AbortToken);

        // then
        await _bus.DidNotReceive().PublishAsync(Arg.Any<FeatureChangedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_write_successfully_when_no_bus_is_registered()
    {
        // given
        const string featureName = "TestFeature";
        var definition = new FeatureDefinition(featureName);
        _definitionManager.FindAsync(featureName, AbortToken).Returns(definition);
        var busless = new FeatureManager(
            _definitionManager,
            _valueProviderManager,
            new DefaultFeatureErrorsDescriptor(),
            _hostIdentity,
            bus: null
        );

        // when
        var act = async () =>
            await busless.SetAsync(
                featureName,
                "value",
                "Provider1",
                providerKey: null,
                forceToSet: true,
                cancellationToken: AbortToken
            );

        // then
        await act.Should().NotThrowAsync();
        await _provider.Received(1).SetAsync(definition, "value", providerKey: null, AbortToken);
    }

    [Fact]
    public async Task should_keep_the_write_when_the_announcement_fails()
    {
        // given
        const string featureName = "TestFeature";
        var definition = new FeatureDefinition(featureName);
        _definitionManager.FindAsync(featureName, AbortToken).Returns(definition);
        _bus.PublishAsync(Arg.Any<FeatureChangedMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("broker down"));

        // when
        var act = async () =>
            await _sut.SetAsync(
                featureName,
                "value",
                "Provider1",
                providerKey: null,
                forceToSet: true,
                cancellationToken: AbortToken
            );

        // then
        await act.Should().NotThrowAsync();
        await _provider.Received(1).SetAsync(definition, "value", providerKey: null, AbortToken);
    }

    [Fact]
    public async Task should_propagate_cancellation_raised_by_the_announcement()
    {
        // given
        const string featureName = "TestFeature";
        var definition = new FeatureDefinition(featureName);
        _definitionManager.FindAsync(featureName, Arg.Any<CancellationToken>()).Returns(definition);

        using var cts = new CancellationTokenSource();
        _bus.PublishAsync(Arg.Any<FeatureChangedMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task<PublishReceipt>>(async _ =>
            {
                await cts.CancelAsync();
                throw new OperationCanceledException(cts.Token);
            });

        // when
        var act = async () =>
            await _sut.SetAsync(
                featureName,
                "value",
                "Provider1",
                providerKey: null,
                forceToSet: true,
                cancellationToken: cts.Token
            );

        // then a caller's cancellation is not a broker failure to swallow
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
