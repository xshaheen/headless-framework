// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.PushNotifications;
using Headless.Sms;

namespace Headless.DevInbox.Dashboard.Internal;

/// <summary>
/// The shapes the module's endpoints and change events serve. A list row is slim: it never carries a body, an HTML
/// document, a payload, or attachment bytes, which show only in the panel on request.
/// </summary>
internal static class DevInboxViews
{
    /// <summary>The longest text a list row quotes from an SMS body or a push title or body.</summary>
    public const int SnippetLength = 120;

    public static string Snippet(string text)
    {
        var line = text.AsSpan();
        var newline = line.IndexOfAny('\r', '\n');

        if (newline >= 0)
        {
            line = line[..newline];
        }

        return line.Length <= SnippetLength ? line.ToString() : $"{line[..SnippetLength]}…";
    }

    public static DevInboxAddressView Address(EmailRequestAddress address) =>
        new(address.EmailAddress, address.DisplayName);

    public static string Encoding(SmsEncoding encoding) =>
        encoding switch
        {
            SmsEncoding.Gsm7 => "gsm7",
            SmsEncoding.Ucs2 => "ucs2",
            _ => encoding.ToString(),
        };

    public static string? Priority(PushNotificationPriority? priority) =>
        priority switch
        {
            null => null,
            _ => priority.Value.ToString(),
        };
}

/// <summary>An email address and its display name.</summary>
internal sealed record DevInboxAddressView(string Address, string? DisplayName);

/// <summary>One page of a channel's list, newest first, and the cursor of the next page when one exists.</summary>
internal sealed record DevInboxList<TRow>(IReadOnlyList<TRow> Items, string? NextCursor);

internal sealed record DevInboxEmailRow
{
    public required Guid Id { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public required string SenderName { get; init; }

    public required DevInboxAddressView From { get; init; }

    /// <summary>The first recipient; <see cref="RecipientCount"/> counts every To, Cc, and Bcc address.</summary>
    public required string? Recipient { get; init; }

    public required int RecipientCount { get; init; }

    public required string Subject { get; init; }

    public required bool HasHtml { get; init; }

    public required int AttachmentCount { get; init; }

    public required bool Failed { get; init; }
}

internal sealed record DevInboxEmailAttachmentView(
    int Index,
    string FileName,
    string ContentType,
    long SizeBytes,
    bool IsOmitted
);

internal sealed record DevInboxEmailDetail
{
    public required Guid Id { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public required string SenderName { get; init; }

    public required DevInboxAddressView From { get; init; }

    public required IReadOnlyList<DevInboxAddressView> To { get; init; }

    public required IReadOnlyList<DevInboxAddressView> Cc { get; init; }

    public required IReadOnlyList<DevInboxAddressView> Bcc { get; init; }

    public required string Subject { get; init; }

    /// <summary>Whether the email has an HTML body, which the preview endpoint serves; the body itself is not here.</summary>
    public required bool HasHtml { get; init; }

    public string? TextBody { get; init; }

    public required IReadOnlyList<DevInboxEmailAttachmentView> Attachments { get; init; }

    public string? FailureError { get; init; }

    public string? TraceId { get; init; }

    public string? CorrelationId { get; init; }
}

internal sealed record DevInboxSmsRow
{
    public required Guid Id { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public required string SenderName { get; init; }

    public required string? Recipient { get; init; }

    public required int RecipientCount { get; init; }

    public required bool IsBulk { get; init; }

    public required string Snippet { get; init; }

    public required string Encoding { get; init; }

    public required int Parts { get; init; }

    public required bool Failed { get; init; }
}

/// <summary>
/// How a carrier would send the text: its encoding (<c>gsm7</c> or <c>ucs2</c>), its length in that encoding's units
/// (GSM-7 septets, an extension character counting two, or UTF-16 code units), and its part count.
/// </summary>
internal sealed record DevInboxSegmentationView(string Encoding, int Length, int Parts);

internal sealed record DevInboxSmsDetail
{
    public required Guid Id { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public required string SenderName { get; init; }

    public required IReadOnlyList<string> Recipients { get; init; }

    public required bool IsBulk { get; init; }

    public required string Text { get; init; }

    public required DevInboxSegmentationView Segmentation { get; init; }

    public string? MessageId { get; init; }

    public IReadOnlyDictionary<string, string?>? Properties { get; init; }

    public string? FailureError { get; init; }

    public string? TraceId { get; init; }

    public string? CorrelationId { get; init; }
}

internal sealed record DevInboxPushRow
{
    public required Guid Id { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public required string SenderName { get; init; }

    /// <summary>The first device token; <see cref="TargetCount"/> counts every one.</summary>
    public required string? Target { get; init; }

    public required int TargetCount { get; init; }

    public string? Title { get; init; }

    public string? Body { get; init; }

    public required bool IsDataOnly { get; init; }

    public required IReadOnlyList<string> DataKeys { get; init; }

    public required int PayloadSizeBytes { get; init; }

    public required bool ExceedsPayloadLimit { get; init; }

    public required bool Failed { get; init; }
}

internal sealed record DevInboxPushDetail
{
    public required Guid Id { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public required string SenderName { get; init; }

    public required IReadOnlyList<string> Targets { get; init; }

    public required bool IsMulticast { get; init; }

    public string? Title { get; init; }

    public string? Body { get; init; }

    public required bool IsDataOnly { get; init; }

    public IReadOnlyDictionary<string, string>? Data { get; init; }

    public string? CollapseKey { get; init; }

    public int? Badge { get; init; }

    public string? Sound { get; init; }

    public string? Priority { get; init; }

    public double? TimeToLiveSeconds { get; init; }

    public required int PayloadSizeBytes { get; init; }

    public required bool ExceedsPayloadLimit { get; init; }

    public required int MaxPayloadBytes { get; init; }

    public string? FailureError { get; init; }

    public string? TraceId { get; init; }

    public string? CorrelationId { get; init; }
}
