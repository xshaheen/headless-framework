// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Context;
using Headless.Features;
using Headless.Features.Resources;
using Headless.Messaging;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests.Values;

public sealed class FeatureManagerUnitOfWorkTests : TestBase
{
    private const string _Writer = "Tenant";
    private const string _Fallback = "Edition";

    private readonly IFeatureDefinitionManager _definitionManager = Substitute.For<IFeatureDefinitionManager>();

    private readonly IFeatureValueProviderManager _valueProviderManager =
        Substitute.For<IFeatureValueProviderManager>();

    private readonly ICurrentTenant _currentTenant = Substitute.For<ICurrentTenant>();
    private readonly IBus _bus = Substitute.For<IBus>();
    private readonly IFeatureValueProvider _writer = Substitute.For<IFeatureValueProvider>();
    private readonly IFeatureValueProvider _fallback = Substitute.For<IFeatureValueProvider>();
    private readonly IUnitOfWork _unit = Substitute.For<IUnitOfWork>();
    private readonly List<Func<ValueTask>> _afterCommit = [];
    private readonly FeatureManager _sut;

    public FeatureManagerUnitOfWorkTests()
    {
        var hostIdentity = Substitute.For<IHostIdentityAccessor>();
        hostIdentity.HostName.Returns("prod/orders-7d");
        _writer.Name.Returns(_Writer);
        _fallback.Name.Returns(_Fallback);
        _writer
            .HandleContextAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IAsyncDisposable>());
        _valueProviderManager.ValueProviders.Returns([_writer, _fallback]);
        _unit.OnCompleted(Arg.Do<Func<ValueTask>>(_afterCommit.Add)).Returns(Substitute.For<IDisposable>());

        _sut = new FeatureManager(
            _definitionManager,
            _valueProviderManager,
            new DefaultFeatureErrorsDescriptor(),
            hostIdentity,
            _currentTenant,
            _bus
        );
    }

    [Fact]
    public async Task should_write_through_the_enlisted_provider_path_and_announce_only_after_commit()
    {
        // given
        _Define("Reports");
        _currentTenant.Id.Returns("tenant-a");
        _currentTenant.Name.Returns("Tenant A");

        // when the write ran in tenant-a, and the unit completes after the code left it
        await _sut.SetAsync(_unit, _Values("Reports", "true"), _Writer, "tenant-a", cancellationToken: AbortToken);
        await _bus.DidNotReceiveWithAnyArgs().PublishAsync(default(FeatureChangedMessage), AbortToken);
        _currentTenant.Id.Returns((string?)null);
        await _CommitAsync();

        // then
        await _writer
            .Received(1)
            .SetAllEnlistedAsync(
                _unit,
                Arg.Is<IReadOnlyList<KeyValuePair<FeatureDefinition, string?>>>(x => x.Single().Value == "true"),
                "tenant-a",
                AbortToken
            );
        await _writer.DidNotReceiveWithAnyArgs().SetAllAsync(default!, default, AbortToken);
        _currentTenant.Received(1).Change("tenant-a", "Tenant A");
        await _bus.Received(1)
            .PublishAsync(
                Arg.Is<FeatureChangedMessage>(m =>
                    m.FeatureNames.Single() == "Reports"
                    && m.ProviderName == _Writer
                    && m.ProviderKey == "tenant-a"
                    && m.OriginHostName == "prod/orders-7d"
                ),
                CancellationToken.None
            );
    }

    [Fact]
    public async Task should_read_the_fallback_inside_the_unit_and_store_nothing_when_it_matches()
    {
        // given a fallback the unit already wrote, which only a read inside the unit can see
        var definition = _Define("Reports");
        _fallback.GetOrDefaultEnlistedAsync(_unit, definition, null, AbortToken).Returns("true");

        // when
        await _sut.SetAsync(_unit, _Values("Reports", "true"), _Writer, "tenant-a", cancellationToken: AbortToken);

        // then
        await _fallback.DidNotReceiveWithAnyArgs().GetOrDefaultAsync(default!, default, AbortToken);
        await _writer
            .Received(1)
            .SetAllEnlistedAsync(
                _unit,
                Arg.Is<IReadOnlyList<KeyValuePair<FeatureDefinition, string?>>>(x => x.Single().Value == null),
                "tenant-a",
                AbortToken
            );
    }

    [Fact]
    public async Task should_read_a_non_store_fallback_through_its_ordinary_read()
    {
        // given a default-value-style provider, whose values do not live in the unit's database
        _Define("Reports");
        _valueProviderManager.ValueProviders.Returns([_writer, new ConstantProvider("true")]);

        // when
        await _sut.SetAsync(_unit, _Values("Reports", "true"), _Writer, "tenant-a", cancellationToken: AbortToken);

        // then the default enlisted read served it, so the inherited value is not stored
        await _writer
            .Received(1)
            .SetAllEnlistedAsync(
                _unit,
                Arg.Is<IReadOnlyList<KeyValuePair<FeatureDefinition, string?>>>(x => x.Single().Value == null),
                "tenant-a",
                AbortToken
            );
    }

    [Fact]
    public async Task should_refuse_a_provider_that_cannot_enlist()
    {
        // given a writer that implements only the autonomous members
        _Define("Reports");
        _valueProviderManager.ValueProviders.Returns([new AutonomousOnlyProvider(_Writer)]);

        // when
        var act = () =>
            _sut.SetAsync(_unit, _Values("Reports", "true"), _Writer, "tenant-a", forceToSet: true, AbortToken);

        // then
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage($"*'{_Writer}'*");
        _afterCommit.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_an_undefined_feature_before_writing_or_registering_work()
    {
        // given
        _definitionManager.FindAsync("Missing", AbortToken).Returns((FeatureDefinition?)null);

        // when
        var act = () =>
            _sut.SetAsync(_unit, _Values("Missing", "true"), _Writer, "tenant-a", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
        await _writer.DidNotReceiveWithAnyArgs().SetAllEnlistedAsync(default!, default!, default, AbortToken);
        _afterCommit.Should().BeEmpty();
    }

    [Fact]
    public async Task should_delete_through_every_writable_provider_and_announce_the_names_once_after_commit()
    {
        // given two writable providers under one name that store overlapping names
        var second = Substitute.For<IFeatureValueProvider>();
        second.Name.Returns(_Writer);
        _valueProviderManager.ValueProviders.Returns([_writer, second, _fallback]);
        _writer.DeleteAllEnlistedAsync(_unit, "tenant-a", AbortToken).Returns(["Reports", "Exports"]);
        second.DeleteAllEnlistedAsync(_unit, "tenant-a", AbortToken).Returns(["Reports"]);

        // when
        await _sut.DeleteAsync(_unit, _Writer, "tenant-a", AbortToken);

        // then
        await _fallback.DidNotReceiveWithAnyArgs().DeleteAllEnlistedAsync(default!, default, AbortToken);
        await _bus.DidNotReceiveWithAnyArgs().PublishAsync(default(FeatureChangedMessage), AbortToken);
        await _CommitAsync();
        await _bus.Received(1)
            .PublishAsync(Arg.Is<FeatureChangedMessage>(m => m.FeatureNames.Count == 2), CancellationToken.None);
    }

    [Fact]
    public async Task should_register_no_announcement_when_an_enlisted_delete_removed_nothing()
    {
        // given
        _writer.DeleteAllEnlistedAsync(_unit, "tenant-a", AbortToken).Returns([]);

        // when
        await _sut.DeleteAsync(_unit, _Writer, "tenant-a", AbortToken);

        // then
        _afterCommit.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_null_unit()
    {
        // when
        var act = () =>
            _sut.SetAsync(null!, _Values("Reports", "true"), _Writer, "tenant-a", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private FeatureDefinition _Define(string name)
    {
        var definition = new FeatureDefinition(name);
        _definitionManager.FindAsync(name, AbortToken).Returns(definition);

        return definition;
    }

    private static Dictionary<string, string?> _Values(string name, string? value)
    {
        return new Dictionary<string, string?>(StringComparer.Ordinal) { [name] = value };
    }

    private async Task _CommitAsync()
    {
        foreach (var work in _afterCommit)
        {
            await work();
        }
    }

    private sealed class ConstantProvider(string value) : IFeatureValueReadProvider
    {
        public string Name => "DefaultValue";

        public Task<IAsyncDisposable> HandleContextAsync(
            string providerName,
            string? providerKey,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IAsyncDisposable>(Substitute.For<IAsyncDisposable>());

        public Task<string?> GetOrDefaultAsync(
            FeatureDefinition feature,
            string? providerKey,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<string?>(value);

        public Task<List<FeatureValue>> GetAllAsync(
            FeatureDefinition[] features,
            string? providerKey = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    private sealed class AutonomousOnlyProvider(string name) : IFeatureValueProvider
    {
        public string Name => name;

        public Task<IAsyncDisposable> HandleContextAsync(
            string providerName,
            string? providerKey,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IAsyncDisposable>(Substitute.For<IAsyncDisposable>());

        public Task<string?> GetOrDefaultAsync(
            FeatureDefinition feature,
            string? providerKey,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<string?>(null);

        public Task<List<FeatureValue>> GetAllAsync(
            FeatureDefinition[] features,
            string? providerKey = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new List<FeatureValue>());

        public Task SetAsync(
            FeatureDefinition feature,
            string value,
            string? providerKey,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task ClearAsync(
            FeatureDefinition feature,
            string? providerKey,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }
}
