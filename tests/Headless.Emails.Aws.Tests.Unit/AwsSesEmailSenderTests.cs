// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Headless.Emails;
using Headless.Emails.Aws;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class AwsSesEmailSenderTests
{
    [Fact]
    public async Task caller_cancellation_should_propagate()
    {
        // given - the caller's own token is cancelled and SES surfaces an OperationCanceledException.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ses = Substitute.For<IAmazonSimpleEmailServiceV2>();
        ses.SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));
        var sender = new AwsSesEmailSender(ses, NullLogger<AwsSesEmailSender>.Instance);

        // when
        var act = async () => await sender.SendAsync(_Request(), cts.Token);

        // then - only the caller's own cancellation propagates.
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task non_caller_cancellation_should_return_failed()
    {
        // given - an OperationCanceledException is raised while the caller's token is NOT cancelled (for
        // example an AWS SDK internal HttpClient timeout surfacing as a TaskCanceledException).
        using var cts = new CancellationTokenSource();

        var ses = Substitute.For<IAmazonSimpleEmailServiceV2>();
        ses.SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var sender = new AwsSesEmailSender(ses, NullLogger<AwsSesEmailSender>.Instance);

        // when
        var response = await sender.SendAsync(_Request(), cts.Token);

        // then - it is a delivery failure, not a caller cancellation, so it is returned rather than thrown.
        response.Success.Should().BeFalse();
        response.FailureError.Should().NotBeNull();
    }

    private static SendSingleEmailRequest _Request()
    {
        return new SendSingleEmailRequest
        {
            From = new EmailRequestAddress("from@example.com"),
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "subject",
            MessageText = "body",
        };
    }
}
