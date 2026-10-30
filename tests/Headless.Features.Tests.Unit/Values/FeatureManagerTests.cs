// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Context;
using Headless.Features;
using Headless.Features.Resources;
using Headless.Messaging;
using Headless.MultiTenancy;
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
            Substitute.For<ICurrentTenant>(),
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
        high.GetAllAsync(Arg.Any<FeatureDefinition[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(static callInfo =>
                [
                    .. callInfo
                        .ArgAt<FeatureDefinition[]>(0)
                        .Select(static d => new FeatureValue(
                            d.Name,
                            string.Equals(d.Name, "FromHigh", StringComparison.Ordinal) ? "h" : null
                        )),
                ]
            );
        low.GetAllAsync(Arg.Any<FeatureDefinition[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(static callInfo =>
                [
                    .. callInfo
                        .ArgAt<FeatureDefinition[]>(0)
                        .Select(static d => new FeatureValue(
                            d.Name,
                            string.Equals(d.Name, "FromLow", StringComparison.Ordinal) ? "l" : null
                        )),
                ]
            );

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
        // one batch read per provider instead of one read per feature per provider
        await high.Received(1).GetAllAsync(Arg.Any<FeatureDefinition[]>(), null, AbortToken);
        await low.Received(1).GetAllAsync(Arg.Any<FeatureDefinition[]>(), null, AbortToken);
        await high.DidNotReceive()
            .GetOrDefaultAsync(Arg.Any<FeatureDefinition>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_ask_lower_providers_only_about_still_unresolved_features()
    {
        // given — High answers every requested name, so Low is never asked
        var high = Substitute.For<IFeatureValueProvider>();
        high.Name.Returns("High");
        var low = Substitute.For<IFeatureValueProvider>();
        low.Name.Returns("Low");
        _valueProviderManager.ValueProviders.Returns([high, low]);
        var feature1 = new FeatureDefinition("Feature1");
        var feature2 = new FeatureDefinition("Feature2");
        _definitionManager.GetFeaturesAsync(AbortToken).Returns([feature1, feature2]);
        high.GetAllAsync(Arg.Any<FeatureDefinition[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([new FeatureValue("Feature1", "a"), new FeatureValue("Feature2", "b")]);

        // when
        var values = await _sut.GetAllAsync(
            new HashSet<string>(StringComparer.Ordinal) { "Feature1", "Feature2" },
            AbortToken
        );

        // then
        values["Feature1"].Value.Should().Be("a");
        values["Feature2"].Value.Should().Be("b");
        await low.DidNotReceive()
            .GetAllAsync(Arg.Any<FeatureDefinition[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
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
                Arg.Is<FeatureChangedMessage>(static m =>
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
        _provider
            .GetAllAsync(Arg.Any<FeatureDefinition[]>(), "key1", AbortToken)
            .Returns([new FeatureValue("Feature1", "a"), new FeatureValue("Feature2", "b")]);

        // when
        await _sut.DeleteAsync("Provider1", "key1", AbortToken);

        // then
        await _bus.Received(1)
            .PublishAsync(
                Arg.Is<FeatureChangedMessage>(static m =>
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
        // this scope stores only Feature1; the fallback holds Feature2 under its own default scope
        _provider
            .GetAllAsync(Arg.Any<FeatureDefinition[]>(), "key1", AbortToken)
            .Returns(static callInfo =>
                [
                    .. callInfo
                        .ArgAt<FeatureDefinition[]>(0)
                        .Select(static d => new FeatureValue(
                            d.Name,
                            string.Equals(d.Name, "Feature1", StringComparison.Ordinal) ? "a" : null
                        )),
                ]
            );
        fallbackProvider
            .GetAllAsync(Arg.Any<FeatureDefinition[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(static callInfo =>
                [
                    .. callInfo
                        .ArgAt<FeatureDefinition[]>(0)
                        .Select(static d => new FeatureValue(
                            d.Name,
                            string.Equals(d.Name, "Feature2", StringComparison.Ordinal) ? "default-b" : null
                        )),
                ]
            );

        // when
        await _sut.DeleteAsync("Provider1", "key1", AbortToken);

        // then Feature2 is neither cleared nor announced: nothing changed for it
        await _provider.DidNotReceive().ClearAsync(feature2, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _bus.Received(1)
            .PublishAsync(
                Arg.Is<FeatureChangedMessage>(static m => m.FeatureNames.Count == 1 && m.FeatureNames[0] == "Feature1"),
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
            Substitute.For<ICurrentTenant>(),
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
        await _provider
            .Received(1)
            .SetAllAsync(
                Arg.Is<IReadOnlyList<KeyValuePair<FeatureDefinition, string?>>>(writes =>
                    writes.Count == 1 && writes[0].Key == definition && writes[0].Value == "value"
                ),
                providerKey: null,
                AbortToken
            );
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
        await _provider
            .Received(1)
            .SetAllAsync(
                Arg.Is<IReadOnlyList<KeyValuePair<FeatureDefinition, string?>>>(writes =>
                    writes.Count == 1 && writes[0].Key == definition && writes[0].Value == "value"
                ),
                providerKey: null,
                AbortToken
            );
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

    [Fact]
    public async Task should_write_a_batch_in_one_provider_call_and_announce_every_name_once()
    {
        // given
        var feature1 = new FeatureDefinition("Feature1");
        var feature2 = new FeatureDefinition("Feature2");
        _definitionManager.FindAsync("Feature1", AbortToken).Returns(feature1);
        _definitionManager.FindAsync("Feature2", AbortToken).Returns(feature2);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Feature1"] = "a",
            ["Feature2"] = null,
        };

        // when
        await _sut.SetAsync(values, "Provider1", "key1", forceToSet: true, cancellationToken: AbortToken);

        // then
        await _provider
            .Received(1)
            .SetAllAsync(
                Arg.Is<IReadOnlyList<KeyValuePair<FeatureDefinition, string?>>>(writes =>
                    writes.Count == 2
                    && writes[0].Key == feature1
                    && writes[0].Value == "a"
                    && writes[1].Key == feature2
                    && writes[1].Value == null
                ),
                "key1",
                AbortToken
            );
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
    public async Task should_write_nothing_when_a_name_in_the_batch_is_not_defined()
    {
        // given
        var feature1 = new FeatureDefinition("Feature1");
        _definitionManager.FindAsync("Feature1", AbortToken).Returns(feature1);
        _definitionManager.FindAsync("Missing", AbortToken).Returns((FeatureDefinition?)null);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { ["Feature1"] = "a", ["Missing"] = "b" };

        // when
        var act = async () =>
            await _sut.SetAsync(values, "Provider1", "key1", forceToSet: true, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
        await _provider
            .DidNotReceive()
            .SetAllAsync(
                Arg.Any<IReadOnlyList<KeyValuePair<FeatureDefinition, string?>>>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        await _bus.DidNotReceive().PublishAsync(Arg.Any<FeatureChangedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_write_nothing_when_a_same_named_provider_is_read_only()
    {
        // given the first "Provider1" is writable and a second one registered under the same name is not
        var feature1 = new FeatureDefinition("Feature1");
        _definitionManager.FindAsync("Feature1", AbortToken).Returns(feature1);
        var readOnlyProvider = Substitute.For<IFeatureValueReadProvider>();
        readOnlyProvider.Name.Returns("Provider1");
        _valueProviderManager.ValueProviders.Returns([_provider, readOnlyProvider]);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { ["Feature1"] = "a" };

        // when
        var act = async () =>
            await _sut.SetAsync(values, "Provider1", "key1", forceToSet: true, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
        await _provider
            .DidNotReceive()
            .SetAllAsync(
                Arg.Any<IReadOnlyList<KeyValuePair<FeatureDefinition, string?>>>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        await _bus.DidNotReceive().PublishAsync(Arg.Any<FeatureChangedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_clear_a_batch_value_that_equals_its_fallback()
    {
        // given
        var feature1 = new FeatureDefinition("Feature1");
        var feature2 = new FeatureDefinition("Feature2");
        _definitionManager.FindAsync("Feature1", Arg.Any<CancellationToken>()).Returns(feature1);
        _definitionManager.FindAsync("Feature2", Arg.Any<CancellationToken>()).Returns(feature2);
        var fallbackProvider = Substitute.For<IFeatureValueReadProvider>();
        fallbackProvider.Name.Returns("Default");
        fallbackProvider.GetOrDefaultAsync(feature1, null, Arg.Any<CancellationToken>()).Returns("same");
        fallbackProvider.GetOrDefaultAsync(feature2, null, Arg.Any<CancellationToken>()).Returns("other");
        _provider
            .HandleContextAsync("Provider1", "key1", Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IAsyncDisposable>());
        _valueProviderManager.ValueProviders.Returns([_provider, fallbackProvider]);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Feature1"] = "same",
            ["Feature2"] = "new",
        };

        // when
        await _sut.SetAsync(values, "Provider1", "key1", cancellationToken: AbortToken);

        // then the value equal to the fallback is cleared so it keeps inheriting; the other is stored
        await _provider
            .Received(1)
            .SetAllAsync(
                Arg.Is<IReadOnlyList<KeyValuePair<FeatureDefinition, string?>>>(static writes =>
                    writes.Count == 2 && writes[0].Value == null && writes[1].Value == "new"
                ),
                "key1",
                AbortToken
            );
    }

    [Fact]
    public async Task should_write_and_announce_nothing_for_an_empty_batch()
    {
        // when
        await _sut.SetAsync(
            new Dictionary<string, string?>(StringComparer.Ordinal),
            "Provider1",
            "key1",
            cancellationToken: AbortToken
        );

        // then
        await _provider
            .DidNotReceive()
            .SetAllAsync(
                Arg.Any<IReadOnlyList<KeyValuePair<FeatureDefinition, string?>>>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        await _bus.DidNotReceive().PublishAsync(Arg.Any<FeatureChangedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_report_the_missing_provider_without_naming_a_batch_feature()
    {
        // given
        _definitionManager.FindAsync("Feature1", AbortToken).Returns(new FeatureDefinition("Feature1"));
        _definitionManager.FindAsync("Feature2", AbortToken).Returns(new FeatureDefinition("Feature2"));
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { ["Feature1"] = "a", ["Feature2"] = "b" };

        // when
        var act = async () =>
            await _sut.SetAsync(values, "Unregistered", "key1", forceToSet: true, cancellationToken: AbortToken);

        // then the error is about the provider, not whichever feature happened to come first
        var error = (await act.Should().ThrowAsync<ConflictException>()).Which.Errors.Should().ContainSingle().Which;
        error.Code.Should().Be("features:provider-not-found");
        error.Params.Should().NotBeNull();
        error.Params!.Should().ContainKey("providerName").WhoseValue.Should().Be("Unregistered");
        error.Params.Should().NotContainKey("featureName");
    }

    [Fact]
    public async Task should_store_and_clear_a_batch_through_a_provider_without_its_own_batch_write()
    {
        // given a provider that only implements the per-value writes, so the manager uses the interface's default
        var feature1 = new FeatureDefinition("Feature1");
        var feature2 = new FeatureDefinition("Feature2");
        var provider = new PerValueFeatureProvider();
        provider.Values["Feature2"] = "old";
        _definitionManager.FindAsync("Feature1", AbortToken).Returns(feature1);
        _definitionManager.FindAsync("Feature2", AbortToken).Returns(feature2);
        _valueProviderManager.ValueProviders.Returns([provider]);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Feature1"] = "a",
            ["Feature2"] = null,
        };

        // when
        await _sut.SetAsync(values, "PerValue", "key1", forceToSet: true, cancellationToken: AbortToken);

        // then
        provider.Values.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("Feature1", "a"));
    }

    private sealed class PerValueFeatureProvider : IFeatureValueProvider
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public string Name => "PerValue";

        public Task<IAsyncDisposable> HandleContextAsync(
            string providerName,
            string? providerKey,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(Substitute.For<IAsyncDisposable>());
        }

        public Task<string?> GetOrDefaultAsync(
            FeatureDefinition feature,
            string? providerKey,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(Values.GetValueOrDefault(feature.Name));
        }

        public Task<List<FeatureValue>> GetAllAsync(
            FeatureDefinition[] features,
            string? providerKey = null,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(
                features.Select(f => new FeatureValue(f.Name, Values.GetValueOrDefault(f.Name))).ToList()
            );
        }

        public Task SetAsync(
            FeatureDefinition feature,
            string value,
            string? providerKey,
            CancellationToken cancellationToken = default
        )
        {
            Values[feature.Name] = value;

            return Task.CompletedTask;
        }

        public Task ClearAsync(
            FeatureDefinition feature,
            string? providerKey,
            CancellationToken cancellationToken = default
        )
        {
            Values.Remove(feature.Name);

            return Task.CompletedTask;
        }
    }
}
