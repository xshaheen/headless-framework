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
    private readonly IAmazonSimpleEmailServiceV2 _ses = Substitute.For<IAmazonSimpleEmailServiceV2>();
    private readonly AwsSesEmailSender _sender;

    public AwsSesEmailSenderTests()
    {
        _sender = new AwsSesEmailSender(_ses, NullLogger<AwsSesEmailSender>.Instance);
    }

    [Fact]
    public async Task no_attachments_should_use_the_simple_content_path()
    {
        var captured = _CaptureRequest(HttpStatusCode.OK);

        var result = await _sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeTrue();
        captured().Content.Simple.Should().NotBeNull();
        captured().Content.Raw.Should().BeNull();
    }

    [Fact]
    public async Task attachments_should_use_the_raw_content_path()
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
    public async Task raw_path_should_carry_bcc_on_the_envelope_and_hide_it_from_the_mime()
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
    public async Task non_success_status_should_return_failed()
    {
        _CaptureRequest(HttpStatusCode.ServiceUnavailable);

        var result = await _sender.SendAsync(_Request(), AbortToken);

        result.Success.Should().BeFalse();
        result.FailureError.Should().NotBeNull();
    }

    [Fact]
    public async Task missing_body_should_throw_before_calling_ses()
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
            .Returns(new SendEmailResponse { HttpStatusCode = status });
        return () => captured!;
    }

    private static SendSingleEmailRequest _Request() =>
        new()
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "subject",
            MessageText = "body",
        };
}
