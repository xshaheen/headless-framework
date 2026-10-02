// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Runtime;
using Headless.Messaging.Testing;
using Headless.Settings;
using Headless.Settings.Definitions;
using Headless.Settings.Models;
using Headless.Settings.Values;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Values;

public sealed class SettingsSnapshotChangeConsumerTests : TestBase
{
    private const string _Theme = "App.Theme";
    private const string _Limit = "App.Limit";

    #region Routing

    [Fact]
    public async Task should_reload_only_the_snapshot_tracking_an_announced_global_setting()
    {
        // given
        var theme = new RecordingEntry(_Theme);
        var limit = new RecordingEntry(_Limit);
        var sut = new SettingsSnapshotChangeConsumer([theme, limit]);

        // when
        await sut.ConsumeAsync(_Context(SettingValueProviderNames.Global, _Theme, "Other.Setting"), AbortToken);

        // then
        var (reason, names) = theme.Reloads.Should().ContainSingle().Subject;
        reason.Should().Be(SettingsSnapshotReloadReason.Message);
        names.Should().Equal(_Theme);
        limit.Reloads.Should().BeEmpty();
    }

    [Fact]
    public async Task should_reload_when_the_announcement_comes_from_this_host()
    {
        // given - the writing process holds snapshots too
        var theme = new RecordingEntry(_Theme);
        var sut = new SettingsSnapshotChangeConsumer([theme]);

        // when
        await sut.ConsumeAsync(
            _Context(SettingValueProviderNames.Global, _Theme, originHostName: Environment.MachineName),
            AbortToken
        );

        // then
        theme.Reloads.Should().ContainSingle();
    }

    [Theory]
    [InlineData(SettingValueProviderNames.User)]
    [InlineData(SettingValueProviderNames.Tenant)]
    [InlineData(SettingValueProviderNames.Configuration)]
    public async Task should_ignore_announcements_at_other_scopes(string providerName)
    {
        // given
        var theme = new RecordingEntry(_Theme);
        var sut = new SettingsSnapshotChangeConsumer([theme]);

        // when
        await sut.ConsumeAsync(_Context(providerName, _Theme), AbortToken);

        // then
        theme.Reloads.Should().BeEmpty();
    }

    [Fact]
    public async Task should_ignore_announcements_naming_only_untracked_settings()
    {
        // given
        var theme = new RecordingEntry(_Theme);
        var sut = new SettingsSnapshotChangeConsumer([theme]);

        // when
        await sut.ConsumeAsync(_Context(SettingValueProviderNames.Global, "Other.Setting"), AbortToken);

        // then
        theme.Reloads.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_reload_every_snapshot_on_each_establishment(bool isReconnect)
    {
        // given
        var theme = new RecordingEntry(_Theme);
        var limit = new RecordingEntry(_Limit);
        var sut = new SettingsSnapshotChangeConsumer([theme, limit]);

        // when
        await sut.OnSubscriptionEstablishedAsync(
            new SubscriptionEstablishedContext(
                SettingsSnapshotChangeConsumer.Identity,
                [SettingChangedMessage.MessageName],
                isReconnect,
                Generation: isReconnect ? 2 : 1
            ),
            AbortToken
        );

        // then
        theme.Reloads.Should().ContainSingle().Which.Reason.Should().Be(SettingsSnapshotReloadReason.Establishment);
        limit.Reloads.Should().ContainSingle().Which.Reason.Should().Be(SettingsSnapshotReloadReason.Establishment);
    }

    #endregion

    #region Messaging

    [Fact]
    public async Task should_update_the_snapshot_when_a_change_is_announced_over_messaging()
    {
        // given
        var stored = new Dictionary<string, string?>(StringComparer.Ordinal) { [_Theme] = "light" };
        await using var harness = await _CreateHarnessAsync(stored, registerSnapshot: true);
        var snapshot = harness.ServiceProvider.GetRequiredService<ISettingsSnapshot<string>>();
        (await snapshot.GetAsync(AbortToken)).Should().Be("light");
        stored[_Theme] = "dark";

        // when
        await harness.Publisher.PublishAsync(
            new SettingChangedMessage
            {
                SettingNames = [_Theme],
                ProviderName = SettingValueProviderNames.Global,
                OriginHostName = "node-a",
            },
            cancellationToken: AbortToken
        );
        await harness.WaitForConsumed<SettingChangedMessage>(cancellationToken: AbortToken);

        // then
        snapshot.TryGetCurrent(out var updated).Should().BeTrue();
        updated.Should().Be("dark");
        snapshot.Revision.Should().Be(2);
    }

    [Fact]
    public async Task should_not_register_the_consumer_without_a_snapshot()
    {
        // given
        await using var harness = await _CreateHarnessAsync(new(StringComparer.Ordinal), registerSnapshot: false);

        // when
        var consumers = harness.ServiceProvider.GetRequiredService<IConsumerRegistry>().GetAll();

        // then
        consumers.Should().NotContain(metadata => metadata.ConsumerType == typeof(SettingsSnapshotChangeConsumer));
    }

    #endregion

    private static async Task<MessagingTestHarness> _CreateHarnessAsync(
        Dictionary<string, string?> stored,
        bool registerSnapshot
    )
    {
        var settingManager = Substitute.For<ISettingManager>();
        settingManager
            .GetAllAsync(
                Arg.Any<HashSet<string>>(),
                SettingValueProviderNames.Global,
                null,
                true,
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
                Task.FromResult<IReadOnlyList<SettingValue>>([
                    .. stored.Select(pair => new SettingValue(pair.Key, pair.Value)),
                ])
            );
        var definitionManager = Substitute.For<ISettingDefinitionManager>();
        definitionManager
            .FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new SettingDefinition(call.Arg<string>()));

        return await MessagingTestHarness.CreateAsync(
            services =>
            {
                services.AddSingleton(settingManager);
                services.AddSingleton(definitionManager);
                services.ConfigureMessaging(static messaging =>
                    messaging.Message<SettingChangedMessage>(SettingChangedMessage.MessageName, "1")
                );

                if (registerSnapshot)
                {
                    services.AddSettingsSnapshot<string>(snapshot =>
                        snapshot.Names(_Theme).Bind(values => values[_Theme] ?? "default")
                    );
                }

                services.AddHeadlessMessaging(setup =>
                {
                    setup.UseInMemory();
                    setup.UseInMemoryStorage();
                    setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
                });
            },
            AbortToken
        );
    }

    private static ConsumeContext<SettingChangedMessage> _Context(
        string providerName,
        string name,
        string? otherName = null,
        string originHostName = "node-a"
    )
    {
        return new()
        {
            Lane = MessageLane.Bus,
            Message = new SettingChangedMessage
            {
                SettingNames = otherName is null ? [name] : [name, otherName],
                ProviderName = providerName,
                OriginHostName = originHostName,
            },
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            MessageName = SettingChangedMessage.MessageName,
        };
    }

    private sealed class RecordingEntry(string name) : ISettingsSnapshotEntry
    {
        public List<(SettingsSnapshotReloadReason Reason, string[]? Names)> Reloads { get; } = [];

        public IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal) { name };

        public TimeSpan Backstop => TimeSpan.FromMinutes(1);

        public bool IsLoaded => true;

        public Task EnsureLoadedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ReloadAsync(
            SettingsSnapshotReloadReason reason,
            IReadOnlyCollection<string>? announcedNames,
            CancellationToken cancellationToken
        )
        {
            Reloads.Add((reason, announcedNames?.ToArray()));

            return Task.CompletedTask;
        }
    }
}
