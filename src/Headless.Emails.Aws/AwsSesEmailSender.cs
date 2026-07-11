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
/// content path. Every SES-side rejection — surfaced by the SDK as a typed
/// <see cref="AmazonSimpleEmailServiceV2Exception"/> (message rejected, account suspended, throttled,
/// quota exceeded, …) — plus any transport fault is returned as a failed
/// <see cref="SendSingleEmailResponse"/>; only <see cref="OperationCanceledException"/> and argument
/// validation propagate. The failed response surfaces the provider's raw error message, but log output
/// deliberately excludes PII (addresses) and the exception message (which can embed a rejected address),
/// recording only the SES error code, HTTP status, and request id.
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
    /// A successful response carrying the SES message id when SES accepts the message; a failed response
    /// (surfacing the provider's error message) for a SES rejection, transport fault, or non-2xx status.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the request has neither an HTML nor a text body (via <see cref="SendSingleEmailRequest.EnsureHasBody"/>).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled during the send.
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
        SendEmailResponse response;

        try
        {
            response = await ses.SendEmailAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Caller cancellation is not a delivery failure — it propagates per the IEmailSender contract.
            throw;
        }
        catch (AmazonSimpleEmailServiceV2Exception ex)
        {
            // The SES SDK signals every service-side rejection as a typed exception. Return it as a failed
            // response (contract: never throw for a provider error). Log only non-PII tracking fields — the
            // exception message can embed the rejected recipient address, so it is surfaced to the caller in
            // the response but never written to a log sink.
            logger.LogSesRejectedEmail(ex.ErrorCode, (int)ex.StatusCode, ex.RequestId);

            return SendSingleEmailResponse.FromException(ex);
        }
        catch (Exception ex)
        {
            // Transport/SDK faults (connection reset, DNS, serialization) also become a failed response.
            // Log the exception type only — its message may carry request detail we do not want in logs.
            logger.LogSesSendFailed(ex.GetType().Name);

            return SendSingleEmailResponse.FromException(ex);
        }

        if (response.HttpStatusCode.IsSuccessStatusCode())
        {
            return SendSingleEmailResponse.Succeeded(response.MessageId);
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

    [LoggerMessage(
        EventId = 2,
        EventName = "SesRejectedEmail",
        Level = LogLevel.Error,
        Message = "Amazon SES rejected the email send. ErrorCode={ErrorCode}, HttpStatusCode={HttpStatusCode}, RequestId={RequestId}"
    )]
    public static partial void LogSesRejectedEmail(
        this ILogger logger,
        string? errorCode,
        int httpStatusCode,
        string? requestId
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "SesSendFailed",
        Level = LogLevel.Error,
        Message = "Failed to send email via Amazon SES. ExceptionType={ExceptionType}"
    )]
    public static partial void LogSesSendFailed(this ILogger logger, string exceptionType);
}
