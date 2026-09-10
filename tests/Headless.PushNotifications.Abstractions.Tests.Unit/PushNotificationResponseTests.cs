// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;

namespace Tests;

public sealed class PushNotificationResponseTests
{
    [Fact]
    public void should_report_success_state_for_succeeded()
    {
        // when
        var response = PushNotificationResponse.Succeeded("client-id", "msg-1");

        // then
        response.Status.Should().Be(PushNotificationResponseStatus.Success);
        response.IsSucceeded().Should().BeTrue();
        response.IsFailed().Should().BeFalse();
        response.IsUnregistered().Should().BeFalse();
        response.MessageId.Should().Be("msg-1");
        response.FailureError.Should().BeNull();
        response.ClientIdentifier.Should().Be("client-id");
    }

    [Fact]
    public void should_report_failure_state_for_failed()
    {
        // when
        var response = PushNotificationResponse.Failed("client-id", "boom");

        // then
        response.Status.Should().Be(PushNotificationResponseStatus.Failure);
        response.IsFailed().Should().BeTrue();
        response.IsSucceeded().Should().BeFalse();
        response.IsUnregistered().Should().BeFalse();
        response.FailureError.Should().Be("boom");
        response.MessageId.Should().BeNull();
    }

    [Fact]
    public void should_report_unregistered_state_for_unregistered()
    {
        // when
        var response = PushNotificationResponse.Unregistered("client-id");

        // then
        response.Status.Should().Be(PushNotificationResponseStatus.Unregistered);
        response.IsUnregistered().Should().BeTrue();
        response.IsSucceeded().Should().BeFalse();
        response.IsFailed().Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void should_throw_when_succeeded_client_identifier_is_blank(string? clientIdentifier)
    {
        // when
        var action = () => PushNotificationResponse.Succeeded(clientIdentifier!, "msg");

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_succeeded_message_id_is_blank()
    {
        // when
        var action = () => PushNotificationResponse.Succeeded("client-id", "");

        // then
        action.Should().Throw<ArgumentException>();
    }
}
