// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.PushNotifications;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>Each channel's detail over the real capture stores.</summary>
public sealed class DetailEndpointTests : TestBase
{
    [Fact]
    public async Task should_show_a_failed_email_with_its_error_and_an_omitted_attachment_with_its_size()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        var large = new byte[DevInboxTestHost.MaxCapturedAttachmentBytes + 1];
        var email = await DevInboxTestHost.SendEmailAsync(
            app,
            "No body",
            AbortToken,
            text: null,
            attachments: [new() { Name = "report.pdf", File = large }]
        );

        // when
        var detail = await DevInboxTestHost.GetJsonAsync(
            client,
            $"{DevInboxTestHost.Api}/emails/{email.Id}",
            AbortToken
        );

        // then
        detail.GetProperty("failureError").GetString().Should().NotBeNullOrEmpty();
        var attachment = detail.GetProperty("attachments")[0];
        attachment.GetProperty("index").GetInt32().Should().Be(0);
        attachment.GetProperty("fileName").GetString().Should().Be("report.pdf");
        attachment.GetProperty("contentType").GetString().Should().Be("application/pdf");
        attachment.GetProperty("sizeBytes").GetInt64().Should().Be(large.Length);
        attachment.GetProperty("isOmitted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task should_show_an_email_with_its_addresses_and_text_and_without_its_html()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        var email = await DevInboxTestHost.SendEmailAsync(app, "Hi", AbortToken, html: "<b>Hi</b>", text: "Hi");

        // when
        using var response = await DevInboxTestHost.GetAsync(
            client,
            $"{DevInboxTestHost.Api}/emails/{email.Id}",
            AbortToken
        );
        var detail = await DevInboxTestHost.ReadAsync(response, AbortToken);

        // then
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        detail.GetProperty("from").GetProperty("address").GetString().Should().Be("app@example.com");
        detail.GetProperty("from").GetProperty("displayName").GetString().Should().Be("The App");
        detail.GetProperty("to")[0].GetProperty("address").GetString().Should().Be("ada@example.com");
        detail.GetProperty("cc")[0].GetProperty("address").GetString().Should().Be("grace@example.com");
        detail.GetProperty("textBody").GetString().Should().Be("Hi");
        detail.GetProperty("hasHtml").GetBoolean().Should().BeTrue();
        detail.TryGetProperty("htmlBody", out _).Should().BeFalse();
        detail.TryGetProperty("failureError", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("Hello", "gsm7", 5, 1)]
    // An extension character is an escape septet plus the character, so the length counts septets, not characters.
    [InlineData("€€", "gsm7", 4, 1)]
    public async Task should_measure_a_gsm7_sms_in_septets(string text, string encoding, int length, int parts)
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        var sms = await DevInboxTestHost.SendSmsAsync(app, text, AbortToken);

        // when
        var detail = await DevInboxTestHost.GetJsonAsync(client, $"{DevInboxTestHost.Api}/sms/{sms.Id}", AbortToken);

        // then
        var segmentation = detail.GetProperty("segmentation");
        segmentation.GetProperty("encoding").GetString().Should().Be(encoding);
        segmentation.GetProperty("length").GetInt32().Should().Be(length);
        segmentation.GetProperty("parts").GetInt32().Should().Be(parts);
        detail.GetProperty("text").GetString().Should().Be(text);
    }

    [Fact]
    public async Task should_split_a_hundred_character_arabic_sms_into_two_ucs2_parts()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        var sms = await DevInboxTestHost.SendSmsAsync(app, new string('م', 100), AbortToken);

        // when
        var detail = await DevInboxTestHost.GetJsonAsync(client, $"{DevInboxTestHost.Api}/sms/{sms.Id}", AbortToken);

        // then
        var segmentation = detail.GetProperty("segmentation");
        segmentation.GetProperty("encoding").GetString().Should().Be("ucs2");
        segmentation.GetProperty("length").GetInt32().Should().Be(100);
        segmentation.GetProperty("parts").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task should_flag_a_push_whose_payload_exceeds_the_provider_limit()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        var push = await DevInboxTestHost.SendPushAsync(
            app,
            new PushNotificationRequest
            {
                Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["blob"] = new('x', 5_000) },
            },
            AbortToken
        );

        // when
        var detail = await DevInboxTestHost.GetJsonAsync(client, $"{DevInboxTestHost.Api}/push/{push.Id}", AbortToken);

        // then
        detail.GetProperty("exceedsPayloadLimit").GetBoolean().Should().BeTrue();
        detail.GetProperty("payloadSizeBytes").GetInt32().Should().BeGreaterThan(4_096);
        detail.GetProperty("maxPayloadBytes").GetInt32().Should().Be(4_096);
        detail.GetProperty("isDataOnly").GetBoolean().Should().BeTrue();
        detail.TryGetProperty("title", out _).Should().BeFalse();
        detail.TryGetProperty("body", out _).Should().BeFalse();
        detail.TryGetProperty("badge", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_show_a_notification_push_with_its_title_body_and_set_delivery_options()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        var push = await DevInboxTestHost.SendPushAsync(
            app,
            new PushNotificationRequest
            {
                Title = "Order shipped",
                Body = "Your order is on its way.",
                Badge = 3,
            },
            AbortToken
        );

        // when
        var detail = await DevInboxTestHost.GetJsonAsync(client, $"{DevInboxTestHost.Api}/push/{push.Id}", AbortToken);

        // then
        detail.GetProperty("title").GetString().Should().Be("Order shipped");
        detail.GetProperty("body").GetString().Should().Be("Your order is on its way.");
        detail.GetProperty("isDataOnly").GetBoolean().Should().BeFalse();
        detail.GetProperty("badge").GetInt32().Should().Be(3);
        detail.TryGetProperty("sound", out _).Should().BeFalse();
        detail
            .GetProperty("targets")
            .EnumerateArray()
            .Select(static t => t.GetString())
            .Should()
            .Equal("device-token-1");
        detail.GetProperty("exceedsPayloadLimit").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("emails")]
    [InlineData("sms")]
    [InlineData("push")]
    public async Task should_answer_not_found_for_an_unknown_or_malformed_id(string channel)
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);

        // when
        using var unknown = await DevInboxTestHost.GetAsync(
            client,
            $"{DevInboxTestHost.Api}/{channel}/{Guid.NewGuid()}",
            AbortToken
        );
        using var malformed = await DevInboxTestHost.GetAsync(
            client,
            $"{DevInboxTestHost.Api}/{channel}/not-a-guid",
            AbortToken
        );

        // then
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        malformed.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("sms")]
    [InlineData("push")]
    public async Task should_forbid_caching_of_sms_and_push_details(string channel)
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        var id = string.Equals(channel, "sms", StringComparison.Ordinal)
            ? (await DevInboxTestHost.SendSmsAsync(app, "Hi", AbortToken)).Id
            : (
                await DevInboxTestHost.SendPushAsync(
                    app,
                    new PushNotificationRequest { Title = "Hi", Body = "There" },
                    AbortToken
                )
            ).Id;

        // when
        using var response = await DevInboxTestHost.GetAsync(
            client,
            $"{DevInboxTestHost.Api}/{channel}/{id}",
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }
}
