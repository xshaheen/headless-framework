// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PulsarHeadersTests : TestBase
{
    [Fact]
    public void should_have_correct_pulsar_key_header_value()
    {
        // given, when, then
        PulsarHeaders.PulsarKey.Should().Be("headless-pulsar-key");
    }

    [Fact]
    public void should_pulsar_key_be_lowercase()
    {
        // given, when, then
        PulsarHeaders.PulsarKey.Should().Be(PulsarHeaders.PulsarKey.ToLowerInvariant());
    }
}
