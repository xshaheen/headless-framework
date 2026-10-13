// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Testing;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class ResetMessagingHarnessExtensionTests : TestBase
{
    [Fact]
    public async Task should_clear_observations_when_harness_is_registered()
    {
        // given - a host provider whose harness has recorded a consumed message
        await using var fixture = new SharedHarnessFixture();
        await fixture.InitializeAsync();
        var harness = fixture.Harness;

        await harness.Publisher.PublishAsync(new AlphaEvent("A1"), cancellationToken: AbortToken);
        await harness.WaitForConsumedAsync<AlphaEvent>(TimeSpan.FromSeconds(5), AbortToken);

        // when
        await harness.ServiceProvider.ResetMessagingHarnessAsync(AbortToken);

        // then
        harness.Published.Should().BeEmpty();
        harness.Consumed.Should().BeEmpty();
    }

    [Fact]
    public async Task should_complete_without_error_when_no_harness_is_registered()
    {
        // given - a host with no messaging at all
        await using var provider = new ServiceCollection().BuildServiceProvider();

        // when
        var act = () => provider.ResetMessagingHarnessAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }
}
