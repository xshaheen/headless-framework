// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Emails.Dev;

/// <summary>
/// Development-time <see cref="IEmailSender"/> that writes email content to a file instead of
/// delivering it to a real SMTP or API endpoint.
/// </summary>
/// <remarks>
/// Each call appends a human-readable representation of the message (headers, subject, body,
/// attachment names) to the file at <paramref name="filePath"/>, separated by a dashed line.
/// No email is ever sent to real recipients.
/// </remarks>
public sealed class DevEmailSender(string filePath) : IEmailSender
{
    private const string _Separator = "--------------------";
    private readonly string _filePath = Argument.IsNotNullOrEmpty(filePath);

    /// <summary>
    /// Appends a text representation of the email to the configured file.
    /// </summary>
    /// <param name="request">The email message to record.</param>
    /// <param name="cancellationToken">Token used to cancel the file-write operation.</param>
    /// <returns>Always returns a successful <see cref="SendSingleEmailResponse"/>.</returns>
    /// <exception cref="System.IO.IOException">
    /// Propagated if the file cannot be written (for example insufficient permissions or a full disk).
    /// </exception>
    public async ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
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

        await File.AppendAllTextAsync(_filePath, sb.ToString(), cancellationToken).ConfigureAwait(false);

        return SendSingleEmailResponse.Succeeded();
    }
}
