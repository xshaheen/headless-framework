// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Context;
using Headless.Messaging;
using Headless.MultiTenancy;
using Headless.Settings;
using Headless.Settings.Resources;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Tests.Fakes;

namespace Tests.Values;

public sealed class SettingManagerUnitOfWorkTests : TestBase
{
    private const string _Writer = "Tenant";
    private const string _Fallback = "Global";

    private readonly ISettingDefinitionManager _definitionManager = Substitute.For<ISettingDefinitionManager>();
    private readonly ISettingValueStore _valueStore = Substitute.For<ISettingValueStore>();

    private readonly ISettingValueProviderManager _valueProviderManager =
        Substitute.For<ISettingValueProviderManager>();

    private readonly ICurrentTenant _currentTenant = Substitute.For<ICurrentTenant>();
    private readonly IBus _bus = Substitute.For<IBus>();
    private readonly ISettingValueProvider _writer = Substitute.For<ISettingValueProvider>();
    private readonly ISettingValueProvider _fallback = Substitute.For<ISettingValueProvider>();
    private readonly IUnitOfWork _unit = Substitute.For<IUnitOfWork>();
    private readonly List<Func<ValueTask>> _afterCommit = [];
    private readonly SettingManager _sut;

    public SettingManagerUnitOfWorkTests()
    {
        var hostIdentity = Substitute.For<IHostIdentityAccessor>();
        hostIdentity.HostName.Returns("prod/orders-7d");
        _writer.Name.Returns(_Writer);
        _fallback.Name.Returns(_Fallback);
        _valueProviderManager.Providers.Returns([_writer, _fallback]);
        _unit.OnCompleted(Arg.Do<Func<ValueTask>>(_afterCommit.Add)).Returns(Substitute.For<IDisposable>());

        _sut = new SettingManager(
            _definitionManager,
            _valueStore,
            _valueProviderManager,
            Substitute.For<ISettingEncryptionService>(),
            new DefaultSettingErrorsDescriptor(),
            hostIdentity,
            _currentTenant,
            _bus
        );
    }

    [Fact]
    public async Task should_write_through_the_enlisted_provider_path_and_announce_only_after_commit()
    {
        // given
        _Define("App.Theme");
        _currentTenant.Id.Returns("tenant-a");
        _currentTenant.Name.Returns("Tenant A");

        // when the write ran in tenant-a, and the unit completes after the code left it
        await _sut.SetAsync(_unit, _Values("App.Theme", "dark"), _Writer, "tenant-a", cancellationToken: AbortToken);
        await _bus.DidNotReceiveWithAnyArgs().PublishAsync(default(SettingChangedMessage), AbortToken);
        _currentTenant.Id.Returns((string?)null);
        await _CommitAsync();

        // then
        await _writer
            .Received(1)
            .SetAllEnlistedAsync(
                _unit,
                Arg.Is<IReadOnlyList<KeyValuePair<SettingDefinition, string?>>>(x => x.Single().Value == "dark"),
                "tenant-a",
                AbortToken
            );
        await _writer.DidNotReceiveWithAnyArgs().SetAllAsync(default!, default, AbortToken);
        _currentTenant.Received(1).Change("tenant-a", "Tenant A");
        await _bus.Received(1)
            .PublishAsync(
                Arg.Is<SettingChangedMessage>(m =>
                    m.SettingNames.Single() == "App.Theme"
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
        var definition = _Define("App.Theme");
        _fallback.GetOrDefaultEnlistedAsync(_unit, definition, null, AbortToken).Returns("dark");

        // when
        await _sut.SetAsync(_unit, _Values("App.Theme", "dark"), _Writer, "tenant-a", cancellationToken: AbortToken);

        // then
        await _fallback.DidNotReceiveWithAnyArgs().GetOrDefaultAsync(default!, default, AbortToken);
        await _writer
            .Received(1)
            .SetAllEnlistedAsync(
                _unit,
                Arg.Is<IReadOnlyList<KeyValuePair<SettingDefinition, string?>>>(x => x.Single().Value == null),
                "tenant-a",
                AbortToken
            );
    }

    [Fact]
    public async Task should_read_a_non_store_fallback_through_its_ordinary_read()
    {
        // given a configuration-style provider, whose values do not live in the unit's database
        var definition = _Define("App.Theme");
        var configuration = new FakeSettingValueProvider { Name = "Configuration" };
        await configuration.SetAsync(definition, "dark", providerKey: null, AbortToken);
        _valueProviderManager.Providers.Returns([_writer, configuration]);

        // when
        await _sut.SetAsync(_unit, _Values("App.Theme", "dark"), _Writer, "tenant-a", cancellationToken: AbortToken);

        // then the default enlisted read served it, so the inherited value is not stored
        await _writer
            .Received(1)
            .SetAllEnlistedAsync(
                _unit,
                Arg.Is<IReadOnlyList<KeyValuePair<SettingDefinition, string?>>>(x => x.Single().Value == null),
                "tenant-a",
                AbortToken
            );
    }

    [Fact]
    public async Task should_refuse_a_provider_that_cannot_enlist()
    {
        // given a writer that implements only the autonomous members
        _Define("App.Theme");
        _valueProviderManager.Providers.Returns([new FakeSettingValueProvider { Name = _Writer }]);

        // when
        var act = () =>
            _sut.SetAsync(_unit, _Values("App.Theme", "dark"), _Writer, "tenant-a", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage($"*'{_Writer}'*");
        _afterCommit.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_an_undefined_setting_before_writing_or_registering_work()
    {
        // given
        _definitionManager.FindAsync("Missing", AbortToken).Returns((SettingDefinition?)null);

        // when
        var act = () =>
            _sut.SetAsync(_unit, _Values("Missing", "x"), _Writer, "tenant-a", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
        await _writer.DidNotReceiveWithAnyArgs().SetAllEnlistedAsync(default!, default!, default, AbortToken);
        _afterCommit.Should().BeEmpty();
    }

    [Fact]
    public async Task should_delete_through_the_store_enlisted_path_and_announce_after_commit()
    {
        // given
        _valueStore.DeleteEnlistedAsync(_unit, _Writer, "tenant-a", AbortToken).Returns(["App.Theme", "App.Locale"]);

        // when
        await _sut.DeleteAsync(_unit, _Writer, "tenant-a", AbortToken);

        // then
        await _valueStore.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default!, default, AbortToken);
        await _bus.DidNotReceiveWithAnyArgs().PublishAsync(default(SettingChangedMessage), AbortToken);
        await _CommitAsync();
        await _bus.Received(1)
            .PublishAsync(Arg.Is<SettingChangedMessage>(m => m.SettingNames.Count == 2), CancellationToken.None);
    }

    [Fact]
    public async Task should_register_no_announcement_when_an_enlisted_delete_removed_nothing()
    {
        // given
        _valueStore.DeleteEnlistedAsync(_unit, _Writer, "tenant-a", AbortToken).Returns([]);

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
            _sut.SetAsync(null!, _Values("App.Theme", "dark"), _Writer, "tenant-a", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private SettingDefinition _Define(string name)
    {
        var definition = new SettingDefinition(name);
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
}
