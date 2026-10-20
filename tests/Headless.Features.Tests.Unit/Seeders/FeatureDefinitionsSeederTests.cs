// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Caching;
using Headless.Context;
using Headless.DistributedLocks;
using Headless.Features;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Seeders;

/// <summary>The feature definitions seeder: its name, its option gate, and the store it saves; the retry is the shared base's.</summary>
public sealed class FeatureDefinitionsSeederTests : TestBase
{
    private readonly IDynamicFeatureDefinitionStore _store = Substitute.For<IDynamicFeatureDefinitionStore>();
    private readonly IServiceProvider _services = Substitute.For<IServiceProvider>();

    public FeatureDefinitionsSeederTests()
    {
        _services.GetService(typeof(IDynamicFeatureDefinitionStore)).Returns(_store);
    }

    [Fact]
    public void should_be_named_feature_definitions()
    {
        ISeeder sut = _CreateSut(saveStatic: true);

        sut.Name.Should().Be("FeatureDefinitions");
    }

    [Fact]
    public async Task should_not_touch_the_store_when_saving_static_definitions_is_disabled()
    {
        var sut = _CreateSut(saveStatic: false);

        await sut.SeedAsync(AbortToken);

        await _store.DidNotReceive().SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_save_the_static_definitions_when_enabled()
    {
        var sut = _CreateSut(saveStatic: true);

        await sut.SeedAsync(AbortToken);

        await _store.Received(1).SaveAsync(AbortToken);
    }

    [Fact]
    public async Task should_report_current_without_writing_or_locking_when_the_store_holds_every_definition()
    {
        // given: the stored records carry other ids, which the comparison ignores
        using var store = new VerifiableStore();
        store.Declare([_Group("Checkout")], [_Feature("Checkout", "Checkout.Enabled")]);
        store.Store([_Group("Checkout")], [_Feature("Checkout", "Checkout.Enabled")]);

        // when
        var state = await store.Seeder.VerifyAsync(AbortToken);

        // then
        state.Should().Be(SeedState.Current);
        store.AssertNothingWrittenOrLocked();
    }

    [Fact]
    public async Task should_report_stale_when_a_declared_feature_is_not_stored()
    {
        using var store = new VerifiableStore();
        store.Declare([_Group("Checkout")], [_Feature("Checkout", "Checkout.Enabled")]);
        store.Store([_Group("Checkout")], []);

        var state = await store.Seeder.VerifyAsync(AbortToken);

        state.Should().Be(SeedState.Stale);
        store.AssertNothingWrittenOrLocked();
    }

    [Fact]
    public async Task should_report_stale_when_a_stored_group_differs_from_its_declaration()
    {
        using var store = new VerifiableStore();
        store.Declare([_Group("Checkout", "Checkout")], []);
        store.Store([_Group("Checkout", "Old checkout")], []);

        var state = await store.Seeder.VerifyAsync(AbortToken);

        state.Should().Be(SeedState.Stale);
    }

    [Fact]
    public async Task should_report_stale_when_a_feature_configured_as_deleted_is_still_stored()
    {
        using var store = new VerifiableStore();
        store.Providers.DeletedFeatures.Add("Checkout.Legacy");
        store.Declare([_Group("Checkout")], []);
        store.Store([_Group("Checkout")], [_Feature("Checkout", "Checkout.Legacy")]);

        var state = await store.Seeder.VerifyAsync(AbortToken);

        state.Should().Be(SeedState.Stale);
    }

    [Fact]
    public async Task should_report_unknown_when_the_application_replaced_the_store()
    {
        var sut = _CreateSut(saveStatic: true);

        var state = await sut.VerifyAsync(AbortToken);

        state.Should().Be(SeedState.Unknown);
        await _store.DidNotReceive().SaveAsync(Arg.Any<CancellationToken>());
    }

    private static FeatureGroupDefinitionRecord _Group(string name, string? displayName = null) =>
        new(Guid.NewGuid(), name, displayName ?? name);

    private static FeatureDefinitionRecord _Feature(string group, string name) =>
        new(Guid.NewGuid(), group, name, parentName: null, displayName: name, defaultValue: "false");

    /// <summary>A real dynamic store over substituted records, so the seeder's comparison runs as it does in a host.</summary>
    private sealed class VerifiableStore : IDisposable
    {
        private readonly DynamicFeatureDefinitionStore _store;

        private readonly IFeatureDefinitionRecordRepository _repository =
            Substitute.For<IFeatureDefinitionRecordRepository>();

        private readonly IFeatureDefinitionSerializer _serializer = Substitute.For<IFeatureDefinitionSerializer>();

        private readonly ICache _cache = Substitute.For<ICache>();

        private readonly IDistributedLock _lock = Substitute.For<IDistributedLock>();

        public VerifiableStore()
        {
            var application = Substitute.For<IHostIdentityAccessor>();
            application.ApplicationName.Returns("TestApp");
            _store = new DynamicFeatureDefinitionStore(
                _repository,
                Substitute.For<IStaticFeatureDefinitionStore>(),
                _serializer,
                _cache,
                _lock,
                Substitute.For<IGuidGenerator>(),
                application,
                Options.Create(new FeatureManagementOptions()),
                Options.Create(Providers),
                TimeProvider.System
            );
            var services = Substitute.For<IServiceProvider>();
            services.GetService(typeof(IDynamicFeatureDefinitionStore)).Returns(_store);
            Seeder = new FeatureDefinitionsSeeder(
                services,
                Options.Create(new FeatureManagementOptions { SaveStaticFeaturesToDatabase = true }),
                NullLogger<FeatureDefinitionsSeeder>.Instance,
                TimeProvider.System
            );
        }

        public FeatureManagementProvidersOptions Providers { get; } = new();

        public FeatureDefinitionsSeeder Seeder { get; }

        public void Declare(List<FeatureGroupDefinitionRecord> groups, List<FeatureDefinitionRecord> features) =>
            _serializer.Serialize(Arg.Any<IEnumerable<FeatureGroupDefinition>>()).Returns((groups, features));

        public void Store(List<FeatureGroupDefinitionRecord> groups, List<FeatureDefinitionRecord> features)
        {
            _repository.GetGroupsListAsync(Arg.Any<CancellationToken>()).Returns(groups);
            _repository.GetFeaturesListAsync(Arg.Any<CancellationToken>()).Returns(features);
        }

        public void Dispose() => _store.Dispose();

        public void AssertNothingWrittenOrLocked()
        {
            _repository
                .ReceivedCalls()
                .Should()
                .OnlyContain(c => c.GetMethodInfo().Name.StartsWith("Get", StringComparison.Ordinal));
            _lock.ReceivedCalls().Should().BeEmpty();
            _cache.ReceivedCalls().Should().BeEmpty();
        }
    }

    private FeatureDefinitionsSeeder _CreateSut(bool saveStatic) =>
        new(
            _services,
            Options.Create(new FeatureManagementOptions { SaveStaticFeaturesToDatabase = saveStatic }),
            NullLogger<FeatureDefinitionsSeeder>.Instance,
            TimeProvider.System
        );
}
