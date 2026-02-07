// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Headless.Emails;
using Microsoft.Extensions.Logging;

namespace Headless.Emails.Aws;

/*
 * API Docs
 * https://docs.aws.amazon.com/ses/latest/APIReference/Welcome.html
 */
public sealed class AwsSesEmailSender(IAmazonSimpleEmailServiceV2 ses, ILogger<AwsSesEmailSender> logger) : IEmailSender
{
    private const string _Charset = "UTF-8";

    public async ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request.Attachments.Count == 0)
        {
            var simpleRequest = _MapToSendEmailRequest(request);

            return await _SendAsync(simpleRequest, cancellationToken);
        }

        using var mimeMessage = await request.ConvertToMimeMessageAsync(cancellationToken);
        await using var memoryStream = new MemoryStream();
        await mimeMessage.WriteToAsync(memoryStream, cancellationToken);

        var rawRequest = new SendEmailRequest
        {
            Content = new EmailContent { Raw = new RawMessage { Data = memoryStream } },
        };

        return await _SendAsync(rawRequest, cancellationToken);
    }

    #region Helpers

    private async Task<SendSingleEmailResponse> _SendAsync(
        SendEmailRequest request,
        CancellationToken cancellationToken
    )
    {
        SendEmailResponse response;

        try
        {
            response = await ses.SendEmailAsync(request, cancellationToken);
        }
        catch (Exception ex)
            when (ex
                    is MessageRejectedException
                        or BadRequestException
                        or NotFoundException
                        or AccountSuspendedException
                        or MailFromDomainNotVerifiedException
                        or LimitExceededException
                        or TooManyRequestsException
                        or SendingPausedException
            )
        {
            throw;
        }

        if (response.HttpStatusCode.IsSuccessStatusCode())
        {
            return SendSingleEmailResponse.Succeeded();
        }

        logger.LogFailedToSendEmail(response);

        return SendSingleEmailResponse.Failed("Failed to send an email to the recipient.");
    }

    private static SendEmailRequest _MapToSendEmailRequest(SendSingleEmailRequest request)
    {
        return new SendEmailRequest
        {
            ConfigurationSetName = null,
            ReplyToAddresses = null,
            FeedbackForwardingEmailAddress = null,
            FromEmailAddress = request.From.ToString(),
            Destination = new Destination
            {
                ToAddresses = request.Destination.ToAddresses.Select(address => address.ToString()).ToList(),
                CcAddresses = request.Destination.CcAddresses.Select(address => address.ToString()).ToList(),
                BccAddresses = request.Destination.BccAddresses.Select(address => address.ToString()).ToList(),
            },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Charset = _Charset, Data = request.Subject },
                    Body = new Body
                    {
                        Html = string.IsNullOrWhiteSpace(request.MessageHtml)
                            ? null
                            : new Content { Charset = _Charset, Data = request.MessageHtml },
                        Text = string.IsNullOrWhiteSpace(request.MessageText)
                            ? null
                            : new Content { Charset = _Charset, Data = request.MessageText },
                    },
                },
            },
        };
    }

    #endregion
}

internal static partial class AwsSesEmailSenderLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToSendEmail",
        Level = LogLevel.Error,
        Message = "Failed to send an email to with response {Response}"
    )]
    public static partial void LogFailedToSendEmail(this ILogger logger, SendEmailResponse response);
}
