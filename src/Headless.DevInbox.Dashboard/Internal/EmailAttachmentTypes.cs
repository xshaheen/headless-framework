// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;

namespace Headless.DevInbox.Dashboard.Internal;

/// <summary>The media type of a captured attachment, as the real MIME converter would send it.</summary>
internal static class EmailAttachmentTypes
{
    /// <summary>
    /// The attachment's captured content type when the caller gave one, else the type its file name implies, reduced
    /// to the lower-case media type without parameters.
    /// </summary>
    public static string Resolve(CapturedEmailAttachment attachment)
    {
        var type = string.IsNullOrWhiteSpace(attachment.ContentType)
            ? string.IsNullOrEmpty(attachment.FileName)
                ? EmailAttachmentContentType.Default
                : EmailAttachmentContentType.Resolve(attachment.FileName)
            : attachment.ContentType;

        var separator = type.IndexOf(';', StringComparison.Ordinal);
        var media = (separator < 0 ? type : type[..separator]).Trim();

#pragma warning disable CA1308 // Media types compare in lower case, and the value is never used for a security decision by case.
        return media.Length == 0 ? EmailAttachmentContentType.Default : media.ToLowerInvariant();
#pragma warning restore CA1308
    }
}
