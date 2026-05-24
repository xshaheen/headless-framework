// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Testing.Tests;

namespace Tests;

public sealed class TransportMessageEqualityTests : TestBase
{
    [Fact]
    public void should_be_equal_when_headers_and_body_match_structurally()
    {
        // given
        var headersA = new Dictionary<string, string?>(StringComparer.Ordinal) { ["a"] = "1", ["b"] = "2" };
        var headersB = new Dictionary<string, string?>(StringComparer.Ordinal) { ["b"] = "2", ["a"] = "1" };
        var bodyA = "hello"u8.ToArray();
        var bodyB = "hello"u8.ToArray();

        // when
        var left = new TransportMessage(headersA, bodyA);
        var right = new TransportMessage(headersB, bodyB);

        // then
        left.Equals(right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
        (left == right).Should().BeTrue();
    }

    [Fact]
    public void should_not_be_equal_when_body_bytes_differ()
    {
        // given
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["a"] = "1" };
        var left = new TransportMessage(headers, "abc"u8.ToArray());
        var right = new TransportMessage(headers, "abd"u8.ToArray());

        // then
        left.Equals(right).Should().BeFalse();
        (left != right).Should().BeTrue();
    }

    [Fact]
    public void should_not_be_equal_when_header_values_differ()
    {
        // given
        var leftHeaders = new Dictionary<string, string?>(StringComparer.Ordinal) { ["a"] = "1" };
        var rightHeaders = new Dictionary<string, string?>(StringComparer.Ordinal) { ["a"] = "2" };

        var left = new TransportMessage(leftHeaders, ReadOnlyMemory<byte>.Empty);
        var right = new TransportMessage(rightHeaders, ReadOnlyMemory<byte>.Empty);

        // then
        left.Equals(right).Should().BeFalse();
    }

    [Fact]
    public void should_not_be_equal_when_header_counts_differ()
    {
        // given
        var leftHeaders = new Dictionary<string, string?>(StringComparer.Ordinal) { ["a"] = "1", ["b"] = "2" };
        var rightHeaders = new Dictionary<string, string?>(StringComparer.Ordinal) { ["a"] = "1" };

        var left = new TransportMessage(leftHeaders, ReadOnlyMemory<byte>.Empty);
        var right = new TransportMessage(rightHeaders, ReadOnlyMemory<byte>.Empty);

        // then
        left.Equals(right).Should().BeFalse();
    }
}
