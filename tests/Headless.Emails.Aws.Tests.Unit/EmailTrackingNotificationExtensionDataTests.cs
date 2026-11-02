// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;
using Headless.Emails.Aws.Tracking;

namespace Tests;

public sealed class EmailTrackingNotificationExtensionDataTests
{
    private const string _Json = """
        {
          "eventType": "Delivery",
          "newSesField": {"nested": 1},
          "mail": {
            "messageId": "m-1",
            "source": "sender@example.com",
            "sendingAccountId": "123",
            "newMailField": "x",
            "commonHeaders": {"subject": "Hi", "newHeaderField": true}
          }
        }
        """;

    [Fact]
    public void should_capture_unknown_fields_when_deserialized_with_reflection()
    {
        var notification = JsonSerializer.Deserialize<EmailTrackingNotification>(_Json);

        _AssertCaptured(notification);
    }

    [Fact]
    public void should_capture_unknown_fields_when_deserialized_with_source_generated_context()
    {
        var notification = JsonSerializer.Deserialize(
            _Json,
            EmailTrackingTestJsonContext.Default.EmailTrackingNotification
        );

        _AssertCaptured(notification);
    }

    private static void _AssertCaptured(EmailTrackingNotification? notification)
    {
        notification.Should().NotBeNull();
        notification.ExtensionData.Should().ContainKey("newSesField");
        notification.Mail.ExtensionData.Should().ContainKey("newMailField");
        notification.Mail.CommonHeaders.Should().NotBeNull();
        notification.Mail.CommonHeaders.ExtensionData.Should().ContainKey("newHeaderField");
    }
}

[JsonSerializable(typeof(EmailTrackingNotification))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class EmailTrackingTestJsonContext : JsonSerializerContext;
