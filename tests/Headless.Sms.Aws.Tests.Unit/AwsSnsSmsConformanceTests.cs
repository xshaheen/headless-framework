// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Headless.Hosting.Options;
using Headless.Sms;
using Headless.Sms.Aws;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;

namespace Tests;

/// <summary>
/// Runs the cross-provider <see cref="SmsSenderConformanceTests"/> contract against the AWS SNS sender.
/// AWS-specific behavior (E.164 formatting, provider message id, non-success status, multi-destination
/// rejection) lives in <see cref="AwsSnsSmsSenderTests"/>.
/// </summary>
public sealed class AwsSnsSmsConformanceTests : SmsSenderConformanceTests
{
    protected override ISmsSender CreateSuccessfulSender()
    {
        var client = Substitute.For<IAmazonSimpleNotificationService>();
        client
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                // Honor the token so the conformance cancellation scenario observes the sender passing it through.
                ci.Arg<CancellationToken>().ThrowIfCancellationRequested();

                return Task.FromResult(new PublishResponse { MessageId = "msg-1", HttpStatusCode = HttpStatusCode.OK });
            });

        return _CreateSender(client);
    }

    protected override ISmsSender CreateFaultingSender()
    {
        var client = Substitute.For<IAmazonSimpleNotificationService>();
        client
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network down"));

        return _CreateSender(client);
    }

    private static AwsSnsSmsSender _CreateSender(IAmazonSimpleNotificationService client)
    {
        var options = new OptionsMonitorWrapper<AwsSnsSmsOptions>(
            new AwsSnsSmsOptions { SenderId = "SENDER", MaxPrice = null }
        );

        return new AwsSnsSmsSender(client, options, optionsName: null, NullLogger<AwsSnsSmsSender>.Instance);
    }

    [Fact]
    public override Task should_reject_a_null_request()
    {
        return base.should_reject_a_null_request();
    }

    [Fact]
    public override Task should_reject_a_null_destination()
    {
        return base.should_reject_a_null_destination();
    }

    [Fact]
    public override Task should_reject_a_request_with_an_empty_body()
    {
        return base.should_reject_a_request_with_an_empty_body();
    }

    [Fact]
    public override Task should_succeed_for_a_single_destination()
    {
        return base.should_succeed_for_a_single_destination();
    }

    [Fact]
    public override Task should_report_a_transient_failure_on_a_transport_fault()
    {
        return base.should_report_a_transient_failure_on_a_transport_fault();
    }

    [Fact]
    public override Task should_propagate_cancellation()
    {
        return base.should_propagate_cancellation();
    }
}
