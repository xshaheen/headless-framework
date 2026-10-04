// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Emails.Dev;

/// <summary>
/// Writes email content to a file instead of delivering messages to an external service.
/// </summary>
/// <remarks>
/// Each send appends a human-readable representation of the message (headers, subject, body, attachment
/// names) to the configured file, separated by a dashed line. The file is created when missing and existing
/// content is preserved. No email is ever sent to real recipients.
/// </remarks>
internal sealed class DevEmailSender(string filePath) : IEmailSender, IDisposable
{
    private const string _Separator = "--------------------";
    private readonly string _filePath = Argument.IsNotNullOrEmpty(filePath);

    // The sender is registered as a singleton; serialize appends so concurrent sends do not
    // interleave output or collide on the file handle.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// Appends the email content to the configured file.
    /// </summary>
    /// <param name="request">The email message to record.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A successful <see cref="SendSingleEmailResponse"/> once the entry is written.</returns>
    /// <exception cref="InvalidOperationException">
    /// The request has neither an HTML nor a text body (the same guard as the real providers).
    /// </exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    /// <exception cref="System.IO.IOException">
    /// The file cannot be written, for example because of insufficient permissions or a full disk.
    /// </exception>
    public async ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        request.EnsureHasBody();

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
            await File.AppendAllTextAsync(_filePath, sb.ToString(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }

        return SendSingleEmailResponse.Succeeded();
    }

    /// <summary>Releases resources used by the write lock.</summary>
    public void Dispose()
    {
        _writeLock.Dispose();
    }
}
