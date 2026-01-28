// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Emails;

namespace Headless.Emails.Dev;

public sealed class DevEmailSender(string filePath) : IEmailSender
{
    private const string _Separator = "--------------------";
    private readonly string _filePath = Argument.IsNotNullOrEmpty(filePath);

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

        await File.AppendAllTextAsync(_filePath, sb.ToString(), cancellationToken);

        return SendSingleEmailResponse.Succeeded();
    }
}
