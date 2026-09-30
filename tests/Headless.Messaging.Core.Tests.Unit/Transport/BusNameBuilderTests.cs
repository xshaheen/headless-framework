// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;

namespace Tests.Transport;

public sealed class BusNameBuilderTests
{
    private static readonly BusNameRules _DotsAllowed = new(50, static c => c is '.' or '-' or '_');
    private static readonly BusNameRules _NoDots = new(255, static c => c is '-' or '_');
    private static readonly BusNameRules _AlphanumericEnds = new(
        50,
        static c => c is '.' or '-' or '_',
        alphanumericBoundaries: true
    );

    [Fact]
    public void should_return_identity_unchanged_when_broker_accepts_it()
    {
        BusNameBuilder.Build("billing.invoice-projection", _DotsAllowed).Should().Be("billing.invoice-projection");
    }

    [Fact]
    public void should_replace_rejected_characters_and_append_hash_when_identity_needs_normalizing()
    {
        BusNameBuilder
            .Build("billing.invoice-projection", _NoDots)
            .Should()
            .Be("billing-invoice-projection-c4edb4b3e4e6");
    }

    [Fact]
    public void should_shorten_to_limit_with_stable_hash_when_identity_is_too_long()
    {
        var identity = "billing." + new string('a', 112);

        var name = BusNameBuilder.Build(identity, _DotsAllowed);

        identity.Should().HaveLength(120);
        name.Should().HaveLength(50);
        name.Should().Be("billing." + new string('a', 29) + "-67bb5fe7f16f");
    }

    [Fact]
    public void should_build_the_same_name_every_time_for_one_identity()
    {
        var identity = "billing." + new string('a', 112);

        BusNameBuilder.Build(identity, _DotsAllowed).Should().Be(BusNameBuilder.Build(identity, _DotsAllowed));
    }

    [Fact]
    public void should_give_distinct_names_when_identities_normalize_to_the_same_prefix()
    {
        var dotted = BusNameBuilder.Build("billing.invoice", _NoDots);
        var dashed = BusNameBuilder.Build("billing-invoice", _NoDots);

        dashed.Should().Be("billing-invoice");
        dotted.Should().NotBe(dashed).And.StartWith("billing-invoice-");
    }

    [Fact]
    public void should_replace_whitespace_and_non_ascii_even_when_rules_allow_everything_else()
    {
        var permissive = new BusNameRules(255, static _ => true);

        BusNameBuilder.Build("Billing Ops.über", permissive).Should().Be("Billing-Ops.-ber-7a940a9793e2");
    }

    [Fact]
    public void should_start_and_end_with_alphanumeric_when_rules_require_it()
    {
        var name = BusNameBuilder.Build("_billing.x_", _AlphanumericEnds);

        name.Should().Be("billing.x-546132bd8e1f");
    }

    [Fact]
    public void should_keep_identity_when_alphanumeric_ends_are_already_met()
    {
        BusNameBuilder.Build("billing.invoice-projection", _AlphanumericEnds).Should().Be("billing.invoice-projection");
    }

    [Fact]
    public void should_fall_back_to_hash_when_no_readable_character_survives()
    {
        BusNameBuilder.Build("__", _AlphanumericEnds).Should().Be("__".ToSha256()[..BusNameBuilder.HashLength]);
    }

    [Fact]
    public void should_reject_limit_without_room_for_the_hash()
    {
        var act = () => new BusNameRules(BusNameBuilder.HashLength + 1, static _ => true);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_blank_identity(string identity)
    {
        var act = () => BusNameBuilder.Build(identity, _DotsAllowed);

        act.Should().Throw<ArgumentException>();
    }
}
