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
    [InlineData("client-id")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_stay_inert_and_report_success_for_single_send(string clientIdentifier)
    {
        // when
        var result = await _service.SendToDeviceAsync(clientIdentifier, _Request, AbortToken);
        // then
        result.IsSucceeded().Should().BeTrue();
        result.MessageId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task should_report_success_for_a_data_only_request()
    {
        // given
        var request = new PushNotificationRequest
        {
            Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["sync"] = "1" },
        };

        // when
        var single = await _service.SendToDeviceAsync("client-id", request, AbortToken);
        var multicast = await _service.SendMulticastAsync(["a", "b"], request, AbortToken);

        // then
        single.IsSucceeded().Should().BeTrue();
        multicast.SuccessCount.Should().Be(2);
        multicast.FailureCount.Should().Be(0);
    }

    [Fact]
    public async Task should_report_success_for_every_multicast_client_identifier()
    {
        // given
        var clientIdentifiers = new[] { "a", "b", "c" };

        // when
        var result = await _service.SendMulticastAsync(clientIdentifiers, _Request, AbortToken);
        // then
        result.SuccessCount.Should().Be(3);
        result.FailureCount.Should().Be(0);
        result.Responses.Should().HaveCount(3);
    }
}
