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
/// deliberately excluded from log output: a rejected request records the HTTP status, the ACS error code,
/// and the <c>x-ms-request-id</c> correlation id; a completed-but-failed terminal status records the send
/// status and operation id. Unrelated exceptions (for example cancellation) propagate. Transient
/// throttling/5xx responses are retried by the <c>Azure.Core</c> pipeline (honoring <c>Retry-After</c>);
/// no custom retry is added. Because the call blocks until a terminal state and that pipeline backs off
/// under throttling — ACS managed-domain limits are low (5/min) — a caller that needs a hard wall-clock
/// bound should pass a cancellation token with a deadline (for example <c>CancellationTokenSource.CancelAfter(...)</c>).
/// </remarks>
internal sealed class AzureCommunicationEmailSender(EmailClient client, ILogger<AzureCommunicationEmailSender> logger)
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
            // x-ms-request-id is the ACS-side correlation handle for the rejected request (no operation id exists yet).
            logger.LogEmailSendRequestFailed(ex.Status, ex.ErrorCode, _TryGetRequestId(ex));

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

    // The ACS-side correlation id for a rejected request; null when the SDK did not attach a raw response.
    private static string? _TryGetRequestId(RequestFailedException exception)
    {
        return
            exception.GetRawResponse() is { } response
            && response.Headers.TryGetValue("x-ms-request-id", out var requestId)
            ? requestId
            : null;
    }
}

internal static partial class AzureCommunicationEmailSenderLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "EmailSendRequestFailed",
        Level = LogLevel.Error,
        Message = "Azure Communication Services rejected the email send. Status={Status}, ErrorCode={ErrorCode}, CorrelationId={CorrelationId}"
    )]
    public static partial void LogEmailSendRequestFailed(
        this ILogger logger,
        int status,
        string? errorCode,
        string? correlationId
    );

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
