// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;
using Headless.Testing.Tests;

namespace Tests.Transport;

/// <summary>
/// A reply address is accepted only inside the reserved namespace, so a forged destination is refused before any
/// broker call and can never fan out through a wildcard.
/// </summary>
public sealed class ReplyAddressesTests : TestBase
{
    [Theory]
    [InlineData("headless.reply.abc")]
    [InlineData("headless.reply.ABC-123_x.y")]
    [InlineData("headless.reply.0")]
    public void should_accept_an_address_with_a_plain_remainder(string address)
    {
        ReplyAddresses.IsInReplyNamespace(address).Should().BeTrue();
    }

    [Fact]
    public void should_accept_an_address_exactly_at_the_maximum_length()
    {
        var address = ReplyAddresses.Prefix + new string('a', ReplyAddresses.MaxLength - ReplyAddresses.Prefix.Length);
        address.Length.Should().Be(ReplyAddresses.MaxLength);

        ReplyAddresses.IsInReplyNamespace(address).Should().BeTrue();
    }

    [Fact]
    public void should_reject_an_address_one_character_over_the_maximum_length()
    {
        var address =
            ReplyAddresses.Prefix + new string('a', ReplyAddresses.MaxLength - ReplyAddresses.Prefix.Length + 1);

        ReplyAddresses.IsInReplyNamespace(address).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("headless.reply.")]
    [InlineData("headless.replies.abc")]
    [InlineData("Headless.reply.abc")]
    [InlineData("orders.created")]
    public void should_reject_an_address_outside_the_namespace_or_with_no_remainder(string? address)
    {
        ReplyAddresses.IsInReplyNamespace(address).Should().BeFalse();
    }

    [Theory]
    [InlineData("headless.reply.a*b")]
    [InlineData("headless.reply.a>")]
    [InlineData("headless.reply.a#b")]
    [InlineData("headless.reply.a b")]
    [InlineData("headless.reply.a/b")]
    [InlineData("headless.reply.ä")]
    public void should_reject_a_remainder_with_a_wildcard_separator_or_non_ascii_character(string address)
    {
        ReplyAddresses.IsInReplyNamespace(address).Should().BeFalse();
    }

    [Fact]
    public void should_create_addresses_inside_the_namespace_that_differ_from_each_other()
    {
        var first = ReplyAddresses.Create();
        var second = ReplyAddresses.Create();

        ReplyAddresses.IsInReplyNamespace(first).Should().BeTrue();
        ReplyAddresses.IsInReplyNamespace(second).Should().BeTrue();
        first.Should().NotBe(second);
    }
}
