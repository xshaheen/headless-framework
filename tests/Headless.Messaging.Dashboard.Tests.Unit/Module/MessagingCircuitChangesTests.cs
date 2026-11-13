// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Messaging;
using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Module;

/// <summary>
/// A circuit lives in one node's memory: its changes go to that node's streams as they happen, and a relaying node
/// reads them by position.
/// </summary>
public sealed class MessagingCircuitChangesTests : TestBase
{
    private const string _Key = "1:orders";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly RecordingEventPublisher _publisher = new();
    private readonly ICircuitBreakerMonitor _monitor = Substitute.For<ICircuitBreakerMonitor>();

    [Fact]
    public async Task should_publish_the_circuit_as_its_list_row_when_the_breaker_reports_a_change()
    {
        // given
        await using var services = _Services();
        var sut = _Create(services);
        _monitor.GetSnapshot(_Key).Returns(_Snapshot(CircuitBreakerState.Closed, failures: 2));

        // when
        sut.OnCircuitChanged(_Key);

        // then: the list row of the circuit carries the same version, so a later read of the list keeps it
        var change = _publisher.Changes.Should().ContainSingle().Which;
        sut.VersionOf(_Key).Should().Be(change.Version);
        sut.VersionOf("1:billing").CompareTo(change.Version).Should().BeNegative();
        change.Target.Module.Should().Be(MessagingDashboardOptions.ModuleId);
        change.Target.Kind.Should().Be(MessagingViews.CircuitKind);
        change.Target.Id.Should().Be("node-a~1:orders");
        change.Target.Node.Should().BeNull();
        var row = change.Projection!.Value;
        row.GetProperty("id").GetString().Should().Be("node-a~1:orders");
        row.GetProperty("consumer").GetString().Should().Be("orders");
        row.GetProperty("state").GetString().Should().Be("Closed");
        row.GetProperty("consecutiveFailures").GetInt32().Should().Be(2);
        // A patch replaces only the fields it carries, so a closed circuit must say it has no open time left.
        row.GetProperty("openedAt").ValueKind.Should().Be(JsonValueKind.Null);
        row.GetProperty("estimatedRemainingOpenDuration").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task should_give_each_change_a_later_version_when_two_changes_share_one_instant()
    {
        // given
        await using var services = _Services();
        var sut = _Create(services);
        _monitor.GetSnapshot(_Key).Returns(_Snapshot(CircuitBreakerState.Open, failures: 5));

        // when: the clock does not move between the two
        sut.OnCircuitChanged(_Key);
        sut.OnCircuitChanged(_Key);

        // then
        var versions = _publisher.Changes.Select(change => change.Version).ToList();
        versions.Should().HaveCount(2).And.OnlyHaveUniqueItems().And.BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public async Task should_publish_no_projection_when_the_node_no_longer_tracks_the_circuit()
    {
        // given
        await using var services = _Services();
        var sut = _Create(services);
        _monitor.GetSnapshot(_Key).Returns((CircuitBreakerSnapshot?)null);

        // when
        sut.OnCircuitChanged(_Key);

        // then
        _publisher.Changes.Should().ContainSingle().Which.Projection.Should().BeNull();
    }

    [Fact]
    public async Task should_return_only_the_circuits_changed_after_the_position_when_a_relaying_node_reads()
    {
        // given
        await using var services = _Services();
        var sut = _Create(services);
        _monitor.GetSnapshot(Arg.Any<string>()).Returns(_Snapshot(CircuitBreakerState.Open, failures: 5));
        sut.OnCircuitChanged("1:billing");
        var position = sut.Position;
        _time.Advance(TimeSpan.FromSeconds(1));
        sut.OnCircuitChanged(_Key);

        // when
        var (changes, next) = sut.ReadSince(position);
        var (later, unchanged) = sut.ReadSince(next);

        // then
        changes.Should().ContainSingle().Which.Target.Id.Should().Be("node-a~1:orders");
        next.Should().Be(sut.Position).And.BeGreaterThan(position);
        later.Should().BeEmpty();
        unchanged.Should().Be(next);
    }

    [Fact]
    public async Task should_return_every_circuit_when_the_position_is_from_before_this_process_started()
    {
        // given: a relaying node still holds a position the node issued before it restarted
        await using var services = _Services();
        var earlierProcessPosition = _time.GetUtcNow().UtcTicks - 1;
        var sut = _Create(services);
        _monitor.KnownConsumers.Returns(new HashSet<string>(StringComparer.Ordinal) { _Key, "1:billing" });
        _monitor.GetSnapshot(Arg.Any<string>()).Returns(_Snapshot(CircuitBreakerState.Closed, failures: 0));

        // when
        var (changes, next) = sut.ReadSince(earlierProcessPosition);

        // then: every circuit goes out as it is now, so the relaying node drops what the earlier process left open
        changes.Select(change => change.Target.Id).Should().BeEquivalentTo("node-a~1:orders", "node-a~1:billing");
        changes.Should().OnlyContain(change => change.Projection != null);
        sut.ReadSince(next).Changes.Should().BeEmpty();
    }

    private ServiceProvider _Services() => new ServiceCollection().AddSingleton(_monitor).BuildServiceProvider();

    private MessagingCircuitChanges _Create(IServiceProvider services) => new("node-a", _publisher, _time, services);

    private static CircuitBreakerSnapshot _Snapshot(CircuitBreakerState state, int failures) =>
        new()
        {
            State = state,
            EscalationLevel = 0,
            OpenedAt = null,
            EstimatedRemainingOpenDuration = null,
            ConsecutiveFailures = failures,
            FailureThreshold = 5,
            EffectiveOpenDuration = TimeSpan.FromSeconds(30),
        };
}
