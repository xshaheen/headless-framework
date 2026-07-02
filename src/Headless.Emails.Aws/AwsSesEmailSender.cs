// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Logging;
using MimeKit;

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
internal sealed class AwsSesEmailSender(IAmazonSimpleEmailServiceV2 ses, ILogger<AwsSesEmailSender> logger)
    : IEmailSender
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
        request.EnsureHasBody();

        if (request.Attachments.Count == 0)
        {
            var simpleRequest = _MapToSendEmailRequest(request);

            return await _SendAsync(simpleRequest, cancellationToken).ConfigureAwait(false);
        }

        using var mimeMessage = await request.ConvertToMimeMessageAsync(cancellationToken).ConfigureAwait(false);

        // SES delivers a raw message to the envelope recipients in Destination. Set them explicitly
        // (To/Cc/Bcc) and hide the Bcc header from the serialized MIME so BCC recipients are never
        // disclosed to the other recipients regardless of how SES treats raw Bcc headers.
        var formatOptions = FormatOptions.Default.Clone();
        formatOptions.HiddenHeaders.Add(HeaderId.Bcc);

        await using var memoryStream = new MemoryStream();
        await mimeMessage.WriteToAsync(formatOptions, memoryStream, cancellationToken).ConfigureAwait(false);

        var rawRequest = new SendEmailRequest
        {
            FromEmailAddress = request.From.ToString(),
            Destination = _MapToDestination(request),
            Content = new EmailContent { Raw = new RawMessage { Data = memoryStream } },
        };

        return await _SendAsync(rawRequest, cancellationToken).ConfigureAwait(false);
    }

    #region Helpers

    private async Task<SendSingleEmailResponse> _SendAsync(
        SendEmailRequest request,
        CancellationToken cancellationToken
    )
    {
        // The SES SDK throws strongly-typed exceptions (MessageRejectedException, BadRequestException,
        // NotFoundException, AccountSuspendedException, MailFromDomainNotVerifiedException,
        // LimitExceededException, TooManyRequestsException, SendingPausedException) for error
        // responses; those propagate to the caller as documented on SendAsync.
        var response = await ses.SendEmailAsync(request, cancellationToken).ConfigureAwait(false);

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
            FromEmailAddress = request.From.ToString(),
            Destination = _MapToDestination(request),
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

    private static Destination _MapToDestination(SendSingleEmailRequest request)
    {
        return new Destination
        {
            ToAddresses = [.. request.Destination.ToAddresses.Select(address => address.ToString())],
            CcAddresses = [.. request.Destination.CcAddresses.Select(address => address.ToString())],
            BccAddresses = [.. request.Destination.BccAddresses.Select(address => address.ToString())],
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
