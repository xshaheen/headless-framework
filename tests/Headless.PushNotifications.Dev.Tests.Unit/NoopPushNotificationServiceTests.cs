// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Headless.PushNotifications.Dev;
using Headless.Testing.Tests;

namespace Tests;

public sealed class NoopPushNotificationServiceTests : TestBase
{
    private static readonly PushNotificationRequest _Request = new() { Title = "title", Body = "body" };

    private readonly NoopPushNotificationService _service = new();

    [Theory]
    [InlineData("token")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_stay_inert_and_report_success_for_single_send(string token)
    {
        // when
        var result = await _service.SendToDeviceAsync(token, _Request, AbortToken);
        // then
        result.IsSucceeded().Should().BeTrue();
        result.MessageId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task should_report_success_for_every_multicast_token()
    {
        // given
        var tokens = new[] { "a", "b", "c" };

        // when
        var result = await _service.SendMulticastAsync(tokens, _Request, AbortToken);
        // then
        result.SuccessCount.Should().Be(3);
        result.FailureCount.Should().Be(0);
        result.Responses.Should().HaveCount(3);
    }
}
