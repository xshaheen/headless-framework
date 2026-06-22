// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Communication.Email;

namespace Headless.Emails.Azure;

/// <summary>
/// Pure mapping from the provider-agnostic <see cref="SendSingleEmailRequest"/> to an Azure
/// Communication Services <see cref="EmailMessage"/>.
/// </summary>
/// <remarks>
/// Extracted as a side-effect-free static so the bulk of provider logic is unit-testable without
/// exercising the ACS long-running send operation. ACS accepts a bare string sender address
/// (<c>senderAddress</c>), so <see cref="EmailRequestAddress.DisplayName"/> on the sender is not honored.
/// </remarks>
internal static class AzureCommunicationEmailMessageMapper
{
    public static EmailMessage ToEmailMessage(SendSingleEmailRequest request)
    {
        var content = new EmailContent(request.Subject) { PlainText = request.MessageText, Html = request.MessageHtml };

        var recipients = new EmailRecipients(request.Destination.ToAddresses.Select(_ToEmailAddress).ToList());

        foreach (var cc in request.Destination.CcAddresses)
        {
            recipients.CC.Add(_ToEmailAddress(cc));
        }

        foreach (var bcc in request.Destination.BccAddresses)
        {
            recipients.BCC.Add(_ToEmailAddress(bcc));
        }

        // ACS senderAddress is a bare string; the sender's display name is not carried.
        var message = new EmailMessage(request.From.EmailAddress, recipients, content);

        foreach (var attachment in request.Attachments)
        {
            message.Attachments.Add(
                new EmailAttachment(
                    attachment.Name,
                    EmailAttachmentContentType.Resolve(attachment.Name),
                    BinaryData.FromBytes(attachment.File)
                )
            );
        }

        return message;
    }

    private static EmailAddress _ToEmailAddress(EmailRequestAddress address)
    {
        return new EmailAddress(address.EmailAddress, address.DisplayName);
    }
}
