// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using Headless.Checks;
using Headless.Emails;

namespace Headless.DevInbox.Dashboard.Internal;

internal sealed class EmailChannel(IEmailCaptureStore store)
    : DevInboxChannel<CapturedEmail, DevInboxEmailRow, DevInboxEmailDetail>
{
    public const string ObjectKind = "email";

    private readonly IEmailCaptureStore _store = Argument.IsNotNull(store);

    public override string Kind => ObjectKind;

    public override string CountKey => "emails";

    public override int Capacity => _store.Capacity;

    public override int Count => _store.Count;

    public override long Version => _store.Version;

    public override JsonTypeInfo<DevInboxEmailRow> RowType => DevInboxJsonContext.Default.DevInboxEmailRow;

    public override JsonTypeInfo<DevInboxEmailDetail> DetailType => DevInboxJsonContext.Default.DevInboxEmailDetail;

    public override bool Delete(Guid id) => _store.Delete(id);

    public override CapturedEmail? Get(Guid id) => _store.Get(id);

    public override IReadOnlyList<CapturedEmail> Query(string? search, long? afterSequence) =>
        _store.Query(new EmailCaptureQuery { Search = search, AfterSequence = afterSequence });

    public override Guid IdOf(CapturedEmail capture) => capture.Id;

    public override long SequenceOf(CapturedEmail capture) => capture.Sequence;

    public override DevInboxEmailRow ToRow(CapturedEmail capture) =>
        new()
        {
            Id = capture.Id,
            Sequence = capture.Sequence,
            CapturedAt = capture.CapturedAt,
            SenderName = capture.SenderName,
            From = DevInboxViews.Address(capture.From),
            Recipient = capture.To.Concat(capture.Cc).Concat(capture.Bcc).FirstOrDefault()?.EmailAddress,
            RecipientCount = capture.To.Count + capture.Cc.Count + capture.Bcc.Count,
            Subject = capture.Subject,
            HasHtml = !string.IsNullOrEmpty(capture.HtmlBody),
            AttachmentCount = capture.Attachments.Count,
            Failed = !capture.Succeeded,
        };

    public override DevInboxEmailDetail ToDetail(CapturedEmail capture) =>
        new()
        {
            Id = capture.Id,
            Sequence = capture.Sequence,
            CapturedAt = capture.CapturedAt,
            SenderName = capture.SenderName,
            From = DevInboxViews.Address(capture.From),
            To = [.. capture.To.Select(DevInboxViews.Address)],
            Cc = [.. capture.Cc.Select(DevInboxViews.Address)],
            Bcc = [.. capture.Bcc.Select(DevInboxViews.Address)],
            Subject = capture.Subject,
            HasHtml = !string.IsNullOrEmpty(capture.HtmlBody),
            TextBody = capture.TextBody,
            Attachments =
            [
                .. capture.Attachments.Select(
                    static (attachment, index) =>
                        new DevInboxEmailAttachmentView(
                            index,
                            attachment.FileName,
                            EmailAttachmentTypes.Resolve(attachment),
                            attachment.SizeBytes,
                            IsOmitted: attachment.Content is null
                        )
                ),
            ],
            FailureError = capture.FailureError,
            TraceId = capture.TraceId,
            CorrelationId = capture.CorrelationId,
        };

    protected override string TitleOf(CapturedEmail capture) => capture.Subject;

    protected override bool FailedOf(CapturedEmail capture) => !capture.Succeeded;

    protected override string? TraceIdOf(CapturedEmail capture) => capture.TraceId;
}
