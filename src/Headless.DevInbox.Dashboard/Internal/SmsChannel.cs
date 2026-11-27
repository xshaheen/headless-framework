// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text.Json.Serialization.Metadata;
using Headless.Checks;
using Headless.Sms;

namespace Headless.DevInbox.Dashboard.Internal;

internal sealed class SmsChannel(ISmsCaptureStore store)
    : DevInboxChannel<CapturedSms, DevInboxSmsRow, DevInboxSmsDetail>
{
    public const string ObjectKind = "sms";

    private readonly ISmsCaptureStore _store = Argument.IsNotNull(store);

    public override string Kind => ObjectKind;

    public override string CountKey => "sms";

    public override int Capacity => _store.Capacity;

    public override int Count => _store.Count;

    public override long Version => _store.Version;

    public override JsonTypeInfo<DevInboxSmsRow> RowType => DevInboxJsonContext.Default.DevInboxSmsRow;

    public override JsonTypeInfo<DevInboxSmsDetail> DetailType => DevInboxJsonContext.Default.DevInboxSmsDetail;

    public override bool Delete(Guid id) => _store.Delete(id);

    public override CapturedSms? Get(Guid id) => _store.Get(id);

    public override IReadOnlyList<CapturedSms> Query(string? search, long? afterSequence) =>
        _store.Query(new SmsCaptureQuery { Search = search, AfterSequence = afterSequence });

    public override Guid IdOf(CapturedSms capture) => capture.Id;

    public override long SequenceOf(CapturedSms capture) => capture.Sequence;

    public override DevInboxSmsRow ToRow(CapturedSms capture) =>
        new()
        {
            Id = capture.Id,
            Sequence = capture.Sequence,
            CapturedAt = capture.CapturedAt,
            SenderName = capture.SenderName,
            Recipient = capture.Destinations.Count > 0 ? capture.Destinations[0].ToString(hasPlusPrefix: true) : null,
            RecipientCount = capture.Destinations.Count,
            IsBulk = capture.IsBulk,
            Snippet = DevInboxViews.Snippet(capture.Text),
            Encoding = DevInboxViews.Encoding(capture.Segmentation.Encoding),
            Parts = capture.Segmentation.SegmentCount,
            Failed = !capture.Succeeded,
        };

    public override DevInboxSmsDetail ToDetail(CapturedSms capture) =>
        new()
        {
            Id = capture.Id,
            Sequence = capture.Sequence,
            CapturedAt = capture.CapturedAt,
            SenderName = capture.SenderName,
            Recipients = [.. capture.Destinations.Select(static d => d.ToString(hasPlusPrefix: true))],
            IsBulk = capture.IsBulk,
            Text = capture.Text,
            Segmentation = new DevInboxSegmentationView(
                DevInboxViews.Encoding(capture.Segmentation.Encoding),
                capture.Segmentation.Length,
                capture.Segmentation.SegmentCount
            ),
            MessageId = capture.MessageId,
            Properties = capture.Properties?.ToDictionary(
                static pair => pair.Key,
                static pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture),
                StringComparer.Ordinal
            ),
            FailureError = capture.FailureError,
            TraceId = capture.TraceId,
            CorrelationId = capture.CorrelationId,
        };

    protected override string TitleOf(CapturedSms capture) => DevInboxViews.Snippet(capture.Text);

    protected override bool FailedOf(CapturedSms capture) => !capture.Succeeded;

    protected override string? TraceIdOf(CapturedSms capture) => capture.TraceId;
}
