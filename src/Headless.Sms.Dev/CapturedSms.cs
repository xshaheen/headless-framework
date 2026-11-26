// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms;

/// <summary>
/// An SMS that a development sender captured instead of delivering, as read from <see cref="ISmsCaptureStore"/>.
/// A bulk send is captured once, with every recipient in <see cref="Destinations"/>.
/// </summary>
/// <remarks>
/// The capture is a snapshot: the recipients and properties are copied when the send runs, so a caller that changes
/// its request afterwards does not change what the store shows.
/// </remarks>
[PublicAPI]
public sealed record CapturedSms
{
    /// <summary>Gets the capture identifier, unique within the process. Order captures by <see cref="Sequence"/>, not by this value.</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the store <see cref="ISmsCaptureStore.Version"/> at which this message was captured. Sequences increase strictly in capture order, so this is the ordering key. Pass it as
    /// <see cref="SmsCaptureQuery.AfterSequence"/> to read only the messages captured after it.
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>Gets the time the send was captured, read from the registered <see cref="TimeProvider"/>. Never earlier than the capture before it.</summary>
    public DateTimeOffset CapturedAt { get; init; }

    /// <summary>
    /// Gets the name of the sender instance that captured the message: the <c>AddNamed</c> name, or an empty string
    /// for the default (unnamed) sender.
    /// </summary>
    public required string SenderName { get; init; }

    /// <summary>Gets the recipients: one for <see cref="ISmsSender"/>, every recipient of a bulk send.</summary>
    public required IReadOnlyList<SmsRequestDestination> Destinations { get; init; }

    /// <summary>Gets a value indicating whether the message came through <see cref="IBulkSmsSender"/>.</summary>
    public bool IsBulk { get; init; }

    /// <summary>Gets the message text.</summary>
    public required string Text { get; init; }

    /// <summary>Gets how a carrier would encode <see cref="Text"/> and how many parts it would send.</summary>
    public SmsSegmentation Segmentation { get; init; }

    /// <summary>Gets the caller-supplied message id, or <see langword="null"/> when the request had none.</summary>
    public string? MessageId { get; init; }

    /// <summary>Gets a copy of the request properties, or <see langword="null"/> when the request had none.</summary>
    public IReadOnlyDictionary<string, object>? Properties { get; init; }

    /// <summary>Gets why the send failed, or <see langword="null"/> when the sender accepted it.</summary>
    public string? FailureError { get; init; }

    /// <summary>Gets a value indicating whether the sender accepted the message.</summary>
    [MemberNotNullWhen(false, nameof(FailureError))]
    public bool Succeeded => FailureError is null;

    /// <summary>
    /// Gets the W3C trace id of the <see cref="System.Diagnostics.Activity"/> that was current during the send, or
    /// <see langword="null"/> when none was.
    /// </summary>
    public string? TraceId { get; init; }

    /// <summary>
    /// Gets the correlation id that the registered <c>ICorrelationIdProvider</c> reported during the send, or
    /// <see langword="null"/> when none is registered or active.
    /// </summary>
    public string? CorrelationId { get; init; }
}
