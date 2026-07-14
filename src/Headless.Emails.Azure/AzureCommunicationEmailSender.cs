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
/// outcome is mapped several ways: a thrown <see cref="RequestFailedException"/>, any other transport/SDK
/// fault, and a completed-but-failed status all produce a failed <see cref="SendSingleEmailResponse"/> that
/// surfaces the provider's error detail (the rejection message, or the terminal status). Only the caller's
/// own cancellation propagates per the <see cref="IEmailSender"/> return-not-throw contract. The failed
/// response may carry the provider's raw message (the calling app owns the recipient data), but log output
/// deliberately excludes recipient and sender addresses: a rejected request records the HTTP status, the ACS
/// error code, and the <c>x-ms-request-id</c> correlation id; a completed-but-failed terminal status records
/// the send status and operation id; any other fault records only the exception type. Transient
/// throttling/5xx responses are retried by the <c>Azure.Core</c> pipeline (honoring <c>Retry-After</c>);
/// no custom retry is added. Because the call blocks until a terminal state and that pipeline backs off
/// under throttling — ACS managed-domain limits are low (5/min) — a caller that needs a hard wall-clock
/// bound should pass a cancellation token with a deadline (for example <c>CancellationTokenSource.CancelAfter(...)</c>).
/// </remarks>
internal sealed class AzureCommunicationEmailSender(EmailClient client, ILogger<AzureCommunicationEmailSender> logger)
    : IEmailSender
{
    /// <summary>
    /// Sends a single email via Azure Communication Services.
    /// </summary>
    /// <param name="request">The email message to send.</param>
    /// <param name="cancellationToken">Token used to cancel the send operation.</param>
    /// <returns>
    /// A successful response (carrying the ACS operation id as the provider message id) when ACS completes
    /// the send with a <c>Succeeded</c> status; a failed response (surfacing the provider's error detail)
    /// when ACS rejects the request (<see cref="RequestFailedException"/>), a transport/SDK fault occurs, or
    /// the send completes with a non-<c>Succeeded</c> terminal status.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled by the caller during the send.
    /// </exception>
    public async ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        // Reject a body-less request identically to the other providers (AWS/Mailkit/Dev all call this first).
        request.EnsureHasBody();

        var message = AzureCommunicationEmailMessageMapper.ToEmailMessage(request);

        EmailSendOperation operation;

        try
        {
            operation = await client.SendAsync(WaitUntil.Completed, message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Only the caller's own cancellation propagates per the IEmailSender contract; every other fault
            // is returned as a failed response by the handlers below.
            throw;
        }
        catch (RequestFailedException ex)
        {
            // Log only non-PII tracking fields — recipient/sender addresses must not leak into log sinks.
            // x-ms-request-id is the ACS-side correlation handle for the rejected request (no operation id exists yet).
            logger.LogEmailSendRequestFailed(ex.Status, ex.ErrorCode, _TryGetRequestId(ex));

            // Surface the provider's raw rejection reason to the caller (parity with the SES/Mailkit
            // providers). The calling app owns the recipient data, so the message is not a leak in the
            // response — only log sinks are kept PII-free (see the non-PII log above).
            return SendSingleEmailResponse.FromException(ex);
        }
        catch (Exception ex)
        {
            // Transport/SDK faults (connection reset, DNS, serialization) also become a failed response per
            // the IEmailSender return-not-throw contract. Log the exception type only — its message may
            // carry connection detail we do not want in log sinks.
            logger.LogEmailSendFailed(ex.GetType().Name);

            return SendSingleEmailResponse.FromException(ex);
        }

        var result = operation.Value;

        if (result.Status == EmailSendStatus.Succeeded)
        {
            // The ACS operation id is the correlation handle for the accepted message (used to query
            // delivery status), so it is surfaced as the provider message id.
            return SendSingleEmailResponse.Succeeded(operation.Id);
        }

        // ACS reached a terminal non-Succeeded state (for example Failed/Canceled) without throwing. Surface
        // the terminal status to the caller for parity with the exception paths; the status is not PII.
        logger.LogEmailCompletedWithFailureStatus(result.Status.ToString(), operation.Id);

        return SendSingleEmailResponse.Failed(
            $"Azure Communication Services completed the send with status '{result.Status}'."
        );
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

    [LoggerMessage(
        EventId = 3,
        EventName = "EmailSendFailed",
        Level = LogLevel.Error,
        Message = "Failed to send email via Azure Communication Services. ExceptionType={ExceptionType}"
    )]
    public static partial void LogEmailSendFailed(this ILogger logger, string exceptionType);
}
