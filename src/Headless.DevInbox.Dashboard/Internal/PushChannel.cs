// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using Headless.Checks;
using Headless.PushNotifications;

namespace Headless.DevInbox.Dashboard.Internal;

internal sealed class PushChannel(IPushNotificationCaptureStore store)
    : DevInboxChannel<CapturedPushNotification, DevInboxPushRow, DevInboxPushDetail>
{
    public const string ObjectKind = "push";

    public const string DataOnlyTitle = "Data-only push";

    private readonly IPushNotificationCaptureStore _store = Argument.IsNotNull(store);

    public override string Kind => ObjectKind;

    public override string CountKey => "push";

    public override int Capacity => _store.Capacity;

    public override int Count => _store.Count;

    public override long Version => _store.Version;

    public override JsonTypeInfo<DevInboxPushRow> RowType => DevInboxJsonContext.Default.DevInboxPushRow;

    public override JsonTypeInfo<DevInboxPushDetail> DetailType => DevInboxJsonContext.Default.DevInboxPushDetail;

    public override bool Delete(Guid id) => _store.Delete(id);

    public override CapturedPushNotification? Get(Guid id) => _store.Get(id);

    public override IReadOnlyList<CapturedPushNotification> Query(string? search, long? afterSequence) =>
        _store.Query(new PushNotificationCaptureQuery { Search = search, AfterSequence = afterSequence });

    public override Guid IdOf(CapturedPushNotification capture) => capture.Id;

    public override long SequenceOf(CapturedPushNotification capture) => capture.Sequence;

    public override DevInboxPushRow ToRow(CapturedPushNotification capture) =>
        new()
        {
            Id = capture.Id,
            Sequence = capture.Sequence,
            CapturedAt = capture.CapturedAt,
            SenderName = capture.SenderName,
            Target = capture.ClientIdentifiers.Count > 0 ? capture.ClientIdentifiers[0] : null,
            TargetCount = capture.ClientIdentifiers.Count,
            Title = capture.Title is null ? null : DevInboxViews.Snippet(capture.Title),
            Body = capture.Body is null ? null : DevInboxViews.Snippet(capture.Body),
            IsDataOnly = capture.IsDataOnly,
            DataKeys = capture.Data is null ? [] : [.. capture.Data.Keys.Order(StringComparer.Ordinal)],
            PayloadSizeBytes = capture.PayloadSizeBytes,
            ExceedsPayloadLimit = capture.ExceedsPayloadLimit,
            Failed = !capture.Succeeded,
        };

    public override DevInboxPushDetail ToDetail(CapturedPushNotification capture) =>
        new()
        {
            Id = capture.Id,
            Sequence = capture.Sequence,
            CapturedAt = capture.CapturedAt,
            SenderName = capture.SenderName,
            Targets = capture.ClientIdentifiers,
            IsMulticast = capture.IsMulticast,
            Title = capture.Title,
            Body = capture.Body,
            IsDataOnly = capture.IsDataOnly,
            Data = capture.Data,
            CollapseKey = capture.CollapseKey,
            Badge = capture.Badge,
            Sound = capture.Sound,
            Priority = DevInboxViews.Priority(capture.Priority),
            TimeToLiveSeconds = capture.TimeToLive?.TotalSeconds,
            PayloadSizeBytes = capture.PayloadSizeBytes,
            ExceedsPayloadLimit = capture.ExceedsPayloadLimit,
            MaxPayloadBytes = CapturedPushNotification.MaxPayloadBytes,
            FailureError = capture.FailureError,
            TraceId = capture.TraceId,
            CorrelationId = capture.CorrelationId,
        };

    protected override string TitleOf(CapturedPushNotification capture) =>
        (capture.Title ?? capture.Body) is { Length: > 0 } title ? DevInboxViews.Snippet(title) : DataOnlyTitle;

    protected override bool FailedOf(CapturedPushNotification capture) => !capture.Succeeded;

    protected override string? TraceIdOf(CapturedPushNotification capture) => capture.TraceId;
}
