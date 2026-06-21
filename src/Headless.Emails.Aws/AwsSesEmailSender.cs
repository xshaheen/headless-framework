// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Logging;

namespace Headless.Emails.Aws;

/*
 * API Docs
 * https://docs.aws.amazon.com/ses/latest/APIReference/Welcome.html
 */
/// <summary>
/// <see cref="IEmailSender"/> implementation backed by Amazon Simple Email Service v2 (SES).
/// </summary>
/// <remarks>
/// When the request contains no attachments the structured SES API path is used
/// (<c>SendEmailRequest</c> with a <c>Simple</c> content block). When attachments are
/// present the message is serialized to a raw MIME stream and sent via the <c>Raw</c>
/// content path. Non-2xx responses that do not throw are surfaced as a failed
/// <see cref="SendSingleEmailResponse"/> with a generic error message; PII (addresses)
/// is deliberately excluded from log output.
/// </remarks>
public sealed class AwsSesEmailSender(IAmazonSimpleEmailServiceV2 ses, ILogger<AwsSesEmailSender> logger) : IEmailSender
{
    private const string _Charset = "UTF-8";

    /// <summary>
    /// Sends a single email via Amazon SES v2.
    /// </summary>
    /// <param name="request">The email message to send.</param>
    /// <param name="cancellationToken">Token used to cancel the send operation.</param>
    /// <returns>
    /// A successful response when SES returns a 2xx status code; a failed response
    /// for non-2xx results that do not map to a thrown exception.
    /// </returns>
    /// <exception cref="Amazon.SimpleEmailV2.Model.MessageRejectedException">
    /// Thrown when SES rejects the message (for example a missing verified sender).
    /// </exception>
    /// <exception cref="Amazon.SimpleEmailV2.Model.BadRequestException">
    /// Thrown when the request is malformed.
    /// </exception>
    /// <exception cref="Amazon.SimpleEmailV2.Model.NotFoundException">
    /// Thrown when a referenced resource (for example a configuration set) does not exist.
    /// </exception>
    /// <exception cref="Amazon.SimpleEmailV2.Model.AccountSuspendedException">
    /// Thrown when the AWS account's email sending capability has been suspended.
    /// </exception>
    /// <exception cref="Amazon.SimpleEmailV2.Model.MailFromDomainNotVerifiedException">
    /// Thrown when the MAIL FROM domain has not been verified with SES.
    /// </exception>
    /// <exception cref="Amazon.SimpleEmailV2.Model.LimitExceededException">
    /// Thrown when the SES sending quota is exceeded.
    /// </exception>
    /// <exception cref="Amazon.SimpleEmailV2.Model.TooManyRequestsException">
    /// Thrown when requests are throttled by SES.
    /// </exception>
    /// <exception cref="Amazon.SimpleEmailV2.Model.SendingPausedException">
    /// Thrown when email sending has been paused for the account or configuration set.
    /// </exception>
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

        // Log only the non-PII tracking fields from the AWS response, not the whole
        // object — recipient/sender addresses must not leak into log sinks.
        logger.LogFailedToSendEmail(
            (int)response.HttpStatusCode,
            response.ResponseMetadata?.RequestId,
            response.MessageId
        );

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
        Message = "Failed to send email. HttpStatusCode={HttpStatusCode}, RequestId={RequestId}, MessageId={MessageId}"
    )]
    public static partial void LogFailedToSendEmail(
        this ILogger logger,
        int httpStatusCode,
        string? requestId,
        string? messageId
    );
}
