// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;
using Headless.Context;

namespace Headless.Emails.Dev;

/// <summary>
/// Captures each email in <see cref="IEmailCaptureStore"/>, and appends a plain-text copy to a file when
/// <see cref="DevEmailOptions.FilePath"/> is set, instead of delivering it.
/// </summary>
/// <remarks>
/// A send that fails (a request with no body, or a file that cannot be written) is captured with its
/// <see cref="CapturedEmail.FailureError"/> before the exception propagates, so the inbox shows failed attempts
/// too. No email is ever sent to real recipients.
/// </remarks>
internal sealed class DevEmailSender(
    DevEmailOptions options,
    EmailCaptureStore store,
    string senderName = "",
    ICorrelationIdProvider? correlationIdProvider = null
) : IEmailSender, IDisposable
{
    private const string _Separator = "--------------------";
    private readonly DevEmailOptions _options = Argument.IsNotNull(options);
    private readonly EmailCaptureStore _store = Argument.IsNotNull(store);
    private readonly string _senderName = Argument.IsNotNull(senderName);

    // The sender is registered as a singleton; serialize appends so concurrent sends do not
    // interleave output or collide on the file handle.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>Captures the email and, when a file is configured, appends it to the file.</summary>
    /// <param name="request">The email message to record.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A successful <see cref="SendSingleEmailResponse"/> whose <see cref="SendSingleEmailResponse.ProviderMessageId"/>
    /// is the <see cref="CapturedEmail.Id"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The request has neither an HTML nor a text body (the same guard as the real providers).
    /// </exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    /// <exception cref="IOException">The file cannot be written, for example because the disk is full.</exception>
    /// <exception cref="UnauthorizedAccessException">The process may not write the file.</exception>
    public async ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            request.EnsureHasBody();
        }
        catch (InvalidOperationException e)
        {
            _Capture(request, e.Message);

            throw;
        }

        if (_options.FilePath is { } filePath)
        {
            try
            {
                await _AppendToFileAsync(filePath, request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _Capture(request, e.Message);

                throw;
            }
        }

        var captured = _Capture(request, failureError: null);

        return SendSingleEmailResponse.Succeeded(captured.Id.ToString());
    }

    /// <summary>Releases resources used by the write lock.</summary>
    public void Dispose()
    {
        _writeLock.Dispose();
    }

    private CapturedEmail _Capture(SendSingleEmailRequest request, string? failureError)
    {
        var maxAttachmentBytes = _options.MaxCapturedAttachmentBytes;

        // Copy every caller-owned collection and buffer so a later change to the request leaves the capture intact.
        var attachments = request
            .Attachments.Select(a => new CapturedEmailAttachment
            {
                FileName = a.Name,
                ContentType = a.ContentType,
                SizeBytes = a.File.Length,
                // A bare null would convert to empty memory through the byte[] conversion, not to "not kept".
                Content = a.File.Length <= maxAttachmentBytes ? a.File.ToArray() : default(ReadOnlyMemory<byte>?),
            })
            .ToArray();

        return _store.Add(
            new CapturedEmail
            {
                SenderName = _senderName,
                From = request.From,
                To = [.. request.Destination.ToAddresses],
                Cc = [.. request.Destination.CcAddresses],
                Bcc = [.. request.Destination.BccAddresses],
                Subject = request.Subject,
                HtmlBody = request.MessageHtml,
                TextBody = request.MessageText,
                Attachments = attachments,
                FailureError = failureError,
                TraceId = Activity.Current?.TraceId.ToString(),
                CorrelationId = correlationIdProvider?.CorrelationId,
            }
        );
    }

    private async Task _AppendToFileAsync(
        string filePath,
        SendSingleEmailRequest request,
        CancellationToken cancellationToken
    )
    {
        var sb = new StringBuilder();

        sb.Append("From: ").AppendLine(request.From.ToString());
        sb.Append("To: ").AppendLine(request.Destination.ToAddresses.JoinAsString(", "));

        if (request.Destination.CcAddresses.Count > 0)
        {
            sb.Append("Cc: ").AppendLine(request.Destination.CcAddresses.JoinAsString(", "));
        }

        if (request.Destination.BccAddresses.Count > 0)
        {
            sb.Append("Bcc: ").AppendLine(request.Destination.BccAddresses.JoinAsString(", "));
        }

        sb.Append("Subject: ").AppendLine(request.Subject);

        if (request.Attachments.Count > 0)
        {
            sb.AppendLine("Attachments:");
            foreach (var attachment in request.Attachments)
            {
                sb.Append("  Name: ").AppendLine(attachment.Name);
            }
        }

        sb.AppendLine("Message:").AppendLine();

        sb.AppendLine(
            !request.MessageText.IsNullOrEmpty()
                ? request.MessageText.RemoveCharacter('\r').Replace("\n", Environment.NewLine, StringComparison.Ordinal)
                : request.MessageHtml
        );

        sb.AppendLine(_Separator);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(filePath, sb.ToString(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
