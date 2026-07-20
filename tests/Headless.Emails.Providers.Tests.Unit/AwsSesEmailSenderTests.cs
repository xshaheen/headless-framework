// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Headless.Emails;
using Headless.Emails.Aws;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class AwsSesEmailSenderTests : TestBase
{
    private const string _MessageId = "ses-message-id-1";
    private readonly IAmazonSimpleEmailServiceV2 _ses = Substitute.For<IAmazonSimpleEmailServiceV2>();
    private readonly AwsSesEmailSender _sender;

    public AwsSesEmailSenderTests()
    {
        _sender = new AwsSesEmailSender(_ses, NullLogger<AwsSesEmailSender>.Instance);
    }

    [Fact]
    public async Task should_use_the_simple_content_path_when_no_attachments()
    {
        var captured = _CaptureRequest(HttpStatusCode.OK);

        var result = await _sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be(_MessageId);
        captured().Content.Simple.Should().NotBeNull();
        captured().Content.Raw.Should().BeNull();
    }

    [Fact]
    public async Task should_use_the_raw_content_path_when_attachments()
    {
        var captured = _CaptureRequest(HttpStatusCode.OK);

        var request = _Request() with
        {
            Attachments = [new EmailRequestAttachment { Name = "a.txt", File = new byte[] { 1, 2, 3 } }],
        };

        var result = await _sender.SendAsync(request, AbortToken);

        result.Success.Should().BeTrue();
        captured().Content.Raw.Should().NotBeNull();
        captured().Content.Simple.Should().BeNull();
    }

    [Fact]
    public async Task should_carry_bcc_on_the_envelope_and_hide_it_from_the_mime_when_raw_path()
    {
        byte[]? rawBytes = null;
        SendEmailRequest? captured = null;
        _ses.SendEmailAsync(
                Arg.Do<SendEmailRequest>(r =>
                {
                    captured = r;
                    if (r.Content.Raw?.Data is { } ms)
                    {
                        rawBytes = ms.ToArray();
                    }
                }),
                Arg.Any<CancellationToken>()
            )
            .Returns(new SendEmailResponse { HttpStatusCode = HttpStatusCode.OK });

        var request = _Request() with
        {
            Destination = new EmailRequestDestination
            {
                ToAddresses = [new EmailRequestAddress("to@example.com")],
                BccAddresses = [new EmailRequestAddress("secret@example.com")],
            },
            Attachments = [new EmailRequestAttachment { Name = "a.txt", File = new byte[] { 1, 2, 3 } }],
        };

        var result = await _sender.SendAsync(request, AbortToken);

        result.Success.Should().BeTrue();
        captured!.Destination.BccAddresses.Should().ContainSingle(a => a.Contains("secret@example.com"));
        Encoding.UTF8.GetString(rawBytes!).Should().NotContain("secret@example.com");
    }

    [Fact]
    public async Task should_return_failed_when_non_success_status()
    {
        _CaptureRequest(HttpStatusCode.ServiceUnavailable);

        var result = await _sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureError.Should().NotBeNull();
    }

    [Fact]
    public async Task should_return_failed_and_surface_the_provider_message_when_ses_typed_exception()
    {
        _ses.SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<SendEmailResponse>(new MessageRejectedException("Email address is not verified"))
            );

        var result = await _sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureError.Should().Be("Email address is not verified");
        result.ProviderMessageId.Should().BeNull();
    }

    [Fact]
    public async Task should_return_failed_when_transport_exception()
    {
        _ses.SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SendEmailResponse>(new HttpRequestException("connection reset")));

        var result = await _sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureError.Should().Be("connection reset");
    }

    [Fact]
    public async Task should_propagate_when_operation_canceled()
    {
        // Only the CALLER's own cancellation propagates. The sender rethrows under
        // `when (cancellationToken.IsCancellationRequested)`, so the caller's token must actually be cancelled --
        // asserting the throw while passing an uncancelled token tests the other branch by accident.
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _ses.SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SendEmailResponse>(new OperationCanceledException()));

        var act = async () => await _sender.SendAsync(_Request(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_return_failed_when_provider_side_cancellation_the_caller_did_not_cancel()
    {
        // The other side of that guard, previously untested: an AWS SDK internal timeout surfaces as a
        // TaskCanceledException while the caller's token is untouched. That is a delivery failure, not a
        // cancellation, so it must become a failed response rather than propagate.
        _ses.SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SendEmailResponse>(new TaskCanceledException("SDK timeout")));

        var result = await _sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task should_throw_before_calling_ses_when_missing_body()
    {
        var request = _Request() with { MessageText = null, MessageHtml = null };

        var act = async () => await _sender.SendAsync(request, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _ses.DidNotReceive().SendEmailAsync(Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>());
    }

    private Func<SendEmailRequest> _CaptureRequest(HttpStatusCode status)
    {
        SendEmailRequest? captured = null;
        _ses.SendEmailAsync(Arg.Do<SendEmailRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new SendEmailResponse { HttpStatusCode = status, MessageId = _MessageId });
        return () => captured!;
    }

    private static SendSingleEmailRequest _Request()
    {
        return new()
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "subject",
            MessageText = "body",
        };
    }
}
