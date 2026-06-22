// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Logging;

namespace Headless.Emails.Azure;

/*
 * API Docs
 * https://learn.microsoft.com/en-us/dotnet/api/overview/azure/communication.email-readme
 */
/// <summary>
/// <see cref="IEmailSender"/> implementation backed by Azure Communication Services (ACS) Email.
/// </summary>
/// <remarks>
/// The send uses <see cref="WaitUntil.Completed"/>, so the call blocks until ACS reaches a terminal
/// state. ACS can complete a send with a non-<c>Succeeded</c> status <em>without</em> throwing, so the
/// outcome is mapped two ways: a thrown <see cref="RequestFailedException"/> and a completed-but-failed
/// status both produce a failed <see cref="SendSingleEmailResponse"/>. Recipient and sender addresses are
/// deliberately excluded from log output; only the operation id, status, and error code are recorded.
/// Unrelated exceptions (for example cancellation) propagate. Transient throttling/5xx responses are
/// retried by the <c>Azure.Core</c> pipeline (honoring <c>Retry-After</c>); no custom retry is added.
/// </remarks>
public sealed class AzureCommunicationEmailSender(EmailClient client, ILogger<AzureCommunicationEmailSender> logger)
    : IEmailSender
{
    private const string _FailureError = "Failed to send an email to the recipient.";

    /// <summary>
    /// Sends a single email via Azure Communication Services.
    /// </summary>
    /// <param name="request">The email message to send.</param>
    /// <param name="cancellationToken">Token used to cancel the send operation.</param>
    /// <returns>
    /// A successful response when ACS completes the send with a <c>Succeeded</c> status; a failed
    /// response when ACS rejects the request (<see cref="RequestFailedException"/>) or completes with a
    /// non-<c>Succeeded</c> terminal status.
    /// </returns>
    public async ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var message = AzureCommunicationEmailMessageMapper.ToEmailMessage(request);

        EmailSendOperation operation;

        try
        {
            operation = await client.SendAsync(WaitUntil.Completed, message, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex)
        {
            // Log only non-PII tracking fields — recipient/sender addresses must not leak into log sinks.
            logger.LogEmailSendRequestFailed(ex.Status, ex.ErrorCode);

            return SendSingleEmailResponse.Failed(_FailureError);
        }

        var result = operation.Value;

        if (result.Status == EmailSendStatus.Succeeded)
        {
            return SendSingleEmailResponse.Succeeded();
        }

        // ACS reached a terminal non-Succeeded state (for example Failed/Canceled) without throwing.
        logger.LogEmailCompletedWithFailureStatus(result.Status.ToString(), operation.Id);

        return SendSingleEmailResponse.Failed(_FailureError);
    }
}

internal static partial class AzureCommunicationEmailSenderLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "EmailSendRequestFailed",
        Level = LogLevel.Error,
        Message = "Azure Communication Services rejected the email send. Status={Status}, ErrorCode={ErrorCode}"
    )]
    public static partial void LogEmailSendRequestFailed(this ILogger logger, int status, string? errorCode);

    [LoggerMessage(
        EventId = 2,
        EventName = "EmailCompletedWithFailureStatus",
        Level = LogLevel.Error,
        Message = "Azure Communication Services completed the email send with a non-success status. Status={Status}, OperationId={OperationId}"
    )]
    public static partial void LogEmailCompletedWithFailureStatus(
        this ILogger logger,
        string status,
        string operationId
    );
}
