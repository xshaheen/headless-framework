// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Checks;

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>Validates a push request and writes it as the APNs JSON payload.</summary>
/// <remarks>
/// Written by hand with <see cref="Utf8JsonWriter"/> rather than a serializer so the output stays AOT-safe and its
/// exact byte length is known for the payload-size check.
/// </remarks>
internal static class ApnsPayloadWriter
{
    // Apple's documented payload limits: 4 KB for regular notifications and 5 KB for VoIP.
    private const int _MaxAlertPayloadBytes = 4096;
    private const int _MaxVoipPayloadBytes = 5120;

    // Apple caps the apns-collapse-id header at 64 bytes.
    private const int _MaxCollapseKeyBytes = 64;

    // The payload's own dictionary, where Apple reads alert, badge, and sound; a custom key with this name would
    // overwrite it.
    private const string _ApsKey = "aps";

    /// <summary>Validates <paramref name="request"/> and returns its UTF-8 JSON payload.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The title or body is blank, a data key is <c>aps</c>, the collapse key exceeds 64 UTF-8 bytes, or the payload
    /// exceeds the push type's size limit.
    /// </exception>
    public static byte[] Write(PushNotificationRequest request, ApnsPushType pushType)
    {
        Argument.IsNotNull(request);
        Argument.IsNotNullOrWhiteSpace(request.Title);
        Argument.IsNotNullOrWhiteSpace(request.Body);

        if (request.CollapseKey is not null)
        {
            Argument.IsLessThanOrEqualTo(Encoding.UTF8.GetByteCount(request.CollapseKey), _MaxCollapseKeyBytes);
        }

        if (request.Data?.ContainsKey(_ApsKey) == true)
        {
            throw new ArgumentException(
                $"Notification data contains the reserved APNs key '{_ApsKey}'.",
                nameof(request)
            );
        }

        var buffer = new ArrayBufferWriter<byte>(512);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartObject(_ApsKey);
            writer.WriteStartObject("alert");
            writer.WriteString("title", request.Title);
            writer.WriteString("body", request.Body);
            writer.WriteEndObject();
            writer.WriteEndObject();

            if (request.Data is not null)
            {
                foreach (var (key, value) in request.Data)
                {
                    writer.WriteString(key, value);
                }
            }

            writer.WriteEndObject();
        }

        var limit = pushType == ApnsPushType.Voip ? _MaxVoipPayloadBytes : _MaxAlertPayloadBytes;

        if (buffer.WrittenCount > limit)
        {
            throw new ArgumentException(
                $"The APNs payload is {buffer.WrittenCount.ToString(CultureInfo.InvariantCulture)} bytes, over the {limit.ToString(CultureInfo.InvariantCulture)}-byte limit for {pushType} pushes.",
                nameof(request)
            );
        }

        return buffer.WrittenSpan.ToArray();
    }
}
