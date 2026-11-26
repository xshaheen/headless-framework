// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;

namespace Headless.PushNotifications.Dev;

/// <summary>Measures the provider-neutral JSON payload of a push notification request.</summary>
internal static class PushNotificationPayload
{
    // Providers send non-ASCII text as raw UTF-8, so measure it that way rather than as \uXXXX escapes.
    private static readonly JsonWriterOptions _WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int MeasureBytes(PushNotificationRequest request)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, _WriterOptions))
        {
            writer.WriteStartObject();

            if (
                request.Title is not null
                || request.Body is not null
                || request.Sound is not null
                || request.Badge is not null
            )
            {
                writer.WriteStartObject("notification");
                _WriteIfNotNull(writer, "title", request.Title);
                _WriteIfNotNull(writer, "body", request.Body);
                _WriteIfNotNull(writer, "sound", request.Sound);

                if (request.Badge is { } badge)
                {
                    writer.WriteNumber("badge", badge);
                }

                writer.WriteEndObject();
            }

            if (request.Data is { Count: > 0 } data)
            {
                writer.WriteStartObject("data");

                foreach (var (key, value) in data)
                {
                    writer.WriteString(key, value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return checked((int)buffer.Length);
    }

    private static void _WriteIfNotNull(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }
}
