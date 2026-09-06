// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Nats;
using Headless.Testing.Tests;
using NATS.Client.JetStream.Models;

namespace Tests;

public sealed class StreamDivergenceReportTests : TestBase
{
    // Comparison

    [Fact]
    public void should_not_compare_a_field_neither_the_provider_nor_the_callback_set()
    {
        // given — the desired config asserts only storage; the live stream carries server-applied defaults
        // that this application never expressed an opinion about.
        var desired = new StreamConfig { Name = "s", Storage = StreamConfigStorage.File };
        var live = new StreamConfig
        {
            Name = "s",
            Storage = StreamConfigStorage.File,
            MaxMsgs = 5_000,
            MaxBytes = 1_024,
            NumReplicas = 3,
        };
        var asserted = new HashSet<string>(StringComparer.Ordinal) { nameof(StreamConfig.Storage) };

        // when
        var divergences = NatsStreamReconciliation.CompareFields(desired, live, asserted);

        // then — the phantom-drift guard: server defaults are not this application's drift
        divergences.Should().BeEmpty();
    }

    [Fact]
    public void should_report_an_asserted_field_that_differs()
    {
        var desired = new StreamConfig { Name = "s", MaxMsgs = 100 };
        var live = new StreamConfig { Name = "s", MaxMsgs = 999 };
        var asserted = new HashSet<string>(StringComparer.Ordinal) { nameof(StreamConfig.MaxMsgs) };

        var divergences = NatsStreamReconciliation.CompareFields(desired, live, asserted);

        divergences.Should().ContainSingle();
        divergences[0].Field.Should().Be(nameof(StreamConfig.MaxMsgs));
        divergences[0].Desired.Should().Be("100");
        divergences[0].Actual.Should().Be("999");
        divergences[0].IsImmutable.Should().BeFalse();
    }

    [Fact]
    public void should_mark_storage_divergence_as_immutable()
    {
        var desired = new StreamConfig { Name = "s", Storage = StreamConfigStorage.File };
        var live = new StreamConfig { Name = "s", Storage = StreamConfigStorage.Memory };
        var asserted = new HashSet<string>(StringComparer.Ordinal) { nameof(StreamConfig.Storage) };

        var divergences = NatsStreamReconciliation.CompareFields(desired, live, asserted);

        divergences.Should().ContainSingle();
        divergences[0].IsImmutable.Should().BeTrue();
    }

    [Fact]
    public void should_identify_only_the_fields_the_callback_changed()
    {
        var config = new StreamConfig { Name = "s", Storage = StreamConfigStorage.File };
        var before = NatsStreamReconciliation.Snapshot(config);

        config.MaxMsgs = 42;

        var asserted = NatsStreamReconciliation.AssertedFields(before, NatsStreamReconciliation.Snapshot(config));

        asserted.Should().Contain(nameof(StreamConfig.MaxMsgs));
        asserted.Should().NotContain(nameof(StreamConfig.NumReplicas));
    }

    // Subject coverage

    [Fact]
    public void should_ignore_subjects_the_live_stream_carries_beyond_what_this_client_needs()
    {
        var uncovered = NatsStreamReconciliation.FindUncoveredSubjects(
            ["headless.bus.orders.created"],
            ["headless.bus.orders.created", "headless.bus.orders.shipped", "headless.bus.invoices.raised"]
        );

        uncovered.Should().BeEmpty();
    }

    [Fact]
    public void should_report_a_required_subject_the_live_stream_does_not_carry()
    {
        var uncovered = NatsStreamReconciliation.FindUncoveredSubjects(
            ["headless.bus.orders.created", "headless.bus.orders.shipped"],
            ["headless.bus.orders.created"]
        );

        uncovered.Should().ContainSingle().Which.Should().Be("headless.bus.orders.shipped");
    }

    [Fact]
    public void should_treat_a_live_wildcard_as_covering_an_exact_subject()
    {
        var uncovered = NatsStreamReconciliation.FindUncoveredSubjects(
            ["headless.bus.orders.created"],
            ["headless.bus.orders.>"]
        );

        uncovered.Should().BeEmpty();
    }

    [Fact]
    public void should_report_every_required_subject_when_the_stream_has_none()
    {
        var uncovered = NatsStreamReconciliation.FindUncoveredSubjects(["a.b", "a.c"], liveSubjects: null);

        uncovered.Should().BeEquivalentTo(["a.b", "a.c"]);
    }

    [Theory]
    [InlineData("a.b", "a.b", true)]
    [InlineData("a.*", "a.b", true)]
    [InlineData("a.*", "a.b.c", false)]
    [InlineData("a.>", "a.b", true)]
    [InlineData("a.>", "a.b.c", true)]
    [InlineData("a.>", "a", false)]
    [InlineData("a.b", "a.c", false)]
    [InlineData("a.b", "a.b.c", false)]
    public void should_match_subjects_by_nats_token_semantics(string pattern, string subject, bool expected)
    {
        NatsStreamReconciliation.Matches(pattern, subject).Should().Be(expected);
    }

    // Message composition

    [Fact]
    public void should_name_the_stream_and_both_values_for_a_single_divergence()
    {
        var message = NatsStreamReconciliation.ComposeDivergenceMessage(
            "headless-bus-orders",
            [new StreamDivergence(nameof(StreamConfig.MaxMsgs), "100", "999", IsImmutable: false)],
            NatsStreamProvisioning.Verify
        );

        message.Should().Contain("headless-bus-orders");
        message.Should().Contain(nameof(StreamConfig.MaxMsgs));
        message.Should().Contain("100");
        message.Should().Contain("999");
    }

    [Fact]
    public void should_list_every_divergent_field_not_only_the_first()
    {
        var message = NatsStreamReconciliation.ComposeDivergenceMessage(
            "s",
            [
                new StreamDivergence(nameof(StreamConfig.MaxMsgs), "100", "999", IsImmutable: false),
                new StreamDivergence(nameof(StreamConfig.NumReplicas), "1", "3", IsImmutable: false),
            ],
            NatsStreamProvisioning.Verify
        );

        message.Should().Contain(nameof(StreamConfig.MaxMsgs));
        message.Should().Contain(nameof(StreamConfig.NumReplicas));
    }

    [Fact]
    public void should_offer_reconcile_as_the_remedy_for_a_mutable_divergence()
    {
        var message = NatsStreamReconciliation.ComposeDivergenceMessage(
            "s",
            [new StreamDivergence(nameof(StreamConfig.MaxMsgs), "100", "999", IsImmutable: false)],
            NatsStreamProvisioning.Verify
        );

        message.Should().Contain(nameof(NatsStreamProvisioning.Reconcile));
    }

    [Fact]
    public void should_not_offer_reconcile_as_the_remedy_for_an_immutable_divergence()
    {
        var message = NatsStreamReconciliation.ComposeDivergenceMessage(
            "s",
            [new StreamDivergence(nameof(StreamConfig.Storage), "File", "Memory", IsImmutable: true)],
            NatsStreamProvisioning.Verify
        );

        message.Should().NotContain(nameof(NatsStreamProvisioning.Reconcile));
        message.Should().Contain("Recreate or migrate");
    }

    [Fact]
    public void should_not_repeat_reconcile_back_to_an_operator_already_reconciling()
    {
        var message = NatsStreamReconciliation.ComposeDivergenceMessage(
            "s",
            [
                new StreamDivergence(nameof(StreamConfig.MaxMsgs), "100", "999", IsImmutable: false),
                new StreamDivergence(nameof(StreamConfig.Storage), "File", "Memory", IsImmutable: true),
            ],
            NatsStreamProvisioning.Reconcile
        );

        message.Should().NotContain($"NatsStreamProvisioning.{nameof(NatsStreamProvisioning.Reconcile)}");
        message.Should().Contain("Recreate or migrate");
    }

    [Fact]
    public void should_state_the_silent_loss_consequence_for_an_uncovered_subject()
    {
        var message = NatsStreamReconciliation.ComposeDivergenceMessage(
            "s",
            [
                new StreamDivergence(
                    nameof(StreamConfig.Subjects),
                    "headless.bus.orders.shipped",
                    "not carried by the stream",
                    IsImmutable: false
                ),
            ],
            NatsStreamProvisioning.Verify
        );

        message.Should().Contain("headless.bus.orders.shipped");
        message.Should().Contain("no messages");
    }
}
