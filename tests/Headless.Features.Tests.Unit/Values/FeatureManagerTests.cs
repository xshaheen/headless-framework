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
        _hostIdentity.InstanceId.Returns("host-a:1");

        _sut = new FeatureManager(
            _definitionManager,
            _valueProviderManager,
            new DefaultFeatureErrorsDescriptor(),
            _hostIdentity,
            _bus
        );
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
                    && m.OriginInstanceId == "host-a:1"
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
