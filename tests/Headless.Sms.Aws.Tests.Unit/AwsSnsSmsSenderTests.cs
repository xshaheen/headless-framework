// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Headless.Sms;
using Headless.Sms.Aws;
using Headless.Sms.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class AwsSnsSmsSenderTests
{
    private static AwsSnsSmsSender CreateSender(IAmazonSimpleNotificationService client)
    {
        var options = Options.Create(new AwsSnsSmsOptions { SenderId = "SENDER", MaxPrice = null });

        return new AwsSnsSmsSender(client, options, NullLogger<AwsSnsSmsSender>.Instance);
    }

    [Fact]
    public async Task should_succeed_and_carry_the_provider_message_id()
    {
        var client = Substitute.For<IAmazonSimpleNotificationService>();
        client
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-1", HttpStatusCode = HttpStatusCode.OK });

        var result = await CreateSender(client).SendAsync(SmsRequests.Single());

        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be("msg-1");
    }

    [Fact]
    public async Task should_send_the_phone_number_in_e164_format()
    {
        var client = Substitute.For<IAmazonSimpleNotificationService>();
        client
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { MessageId = "msg-1", HttpStatusCode = HttpStatusCode.OK });

        await CreateSender(client).SendAsync(SmsRequests.Single(code: 20, number: "1001234567"));

        await client
            .Received(1)
            .PublishAsync(Arg.Is<PublishRequest>(r => r.PhoneNumber == "+201001234567"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reject_multiple_destinations_as_unsupported_without_calling_aws()
    {
        var client = Substitute.For<IAmazonSimpleNotificationService>();

        var result = await CreateSender(client).SendAsync(SmsRequests.Batch("hi", (20, "1"), (20, "2")));

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(SmsFailureKind.Unsupported);
        await client.DidNotReceive().PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_fail_on_non_success_status_code()
    {
        var client = Substitute.For<IAmazonSimpleNotificationService>();
        client
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PublishResponse { HttpStatusCode = HttpStatusCode.BadRequest });

        var result = await CreateSender(client).SendAsync(SmsRequests.Single());

        result.Success.Should().BeFalse();
    }
}
