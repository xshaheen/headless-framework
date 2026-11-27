// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.PushNotifications;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>Each channel's list over the real capture stores: newest first, paged by sequence, searched, never cached.</summary>
public sealed class ListEndpointTests : TestBase
{
    [Fact]
    public async Task should_page_emails_newest_first_by_their_sequence()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        await DevInboxTestHost.SendEmailAsync(app, "first", AbortToken);
        await DevInboxTestHost.SendEmailAsync(app, "second", AbortToken);
        await DevInboxTestHost.SendEmailAsync(app, "third", AbortToken);

        // when
        var page = await DevInboxTestHost.GetJsonAsync(client, DevInboxTestHost.Api + "/emails?limit=2", AbortToken);
        var cursor = page.GetProperty("nextCursor").GetString();
        var rest = await DevInboxTestHost.GetJsonAsync(
            client,
            $"{DevInboxTestHost.Api}/emails?limit=2&before={cursor}",
            AbortToken
        );

        // then
        _Subjects(page).Should().Equal("third", "second");
        _Subjects(rest).Should().Equal("first");
        rest.TryGetProperty("nextCursor", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_return_only_the_email_whose_subject_holds_the_search_ignoring_case()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        await DevInboxTestHost.SendEmailAsync(app, "Your Invoice is ready", AbortToken, text: "Pay soon.");
        await DevInboxTestHost.SendEmailAsync(app, "Welcome aboard", AbortToken, text: "Hi.");

        // when
        var page = await DevInboxTestHost.GetJsonAsync(
            client,
            DevInboxTestHost.Api + "/emails?search=invoice",
            AbortToken
        );

        // then
        _Subjects(page).Should().Equal("Your Invoice is ready");
    }

    [Theory]
    [InlineData(0, "minimum")]
    [InlineData(201, "maximum")]
    public async Task should_refuse_a_limit_outside_one_to_two_hundred_naming_limit(int limit, string rule)
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);

        // when
        using var response = await DevInboxTestHost.GetAsync(
            client,
            $"{DevInboxTestHost.Api}/emails?limit={limit}",
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await DevInboxTestHost.ReadAsync(response, AbortToken);
        problem.GetProperty("rules").GetProperty("limit").GetString().Should().Be(rule);
    }

    [Fact]
    public async Task should_keep_bodies_and_attachment_content_out_of_an_email_row()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        await DevInboxTestHost.SendEmailAsync(
            app,
            "Reset your password",
            AbortToken,
            html: "<p>Reset: https://example.com/reset?token=secret</p>",
            text: "Reset: https://example.com/reset?token=secret",
            attachments: [new() { Name = "a.txt", File = "secret"u8.ToArray() }]
        );

        // when
        var page = await DevInboxTestHost.GetJsonAsync(client, DevInboxTestHost.Api + "/emails", AbortToken);

        // then
        var row = page.GetProperty("items")[0];
        row.TryGetProperty("htmlBody", out _).Should().BeFalse();
        row.TryGetProperty("textBody", out _).Should().BeFalse();
        row.TryGetProperty("attachments", out _).Should().BeFalse();
        row.GetRawText().Should().NotContain("secret");
        row.GetProperty("attachmentCount").GetInt32().Should().Be(1);
        row.GetProperty("hasHtml").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task should_list_sms_and_push_rows_with_their_markers()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        await DevInboxTestHost.SendSmsAsync(app, "Your code is 123456", AbortToken);
        await DevInboxTestHost.SendPushAsync(
            app,
            new PushNotificationRequest
            {
                Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["orderId"] = "42" },
            },
            AbortToken
        );

        // when
        var sms = await DevInboxTestHost.GetJsonAsync(client, DevInboxTestHost.Api + "/sms", AbortToken);
        var push = await DevInboxTestHost.GetJsonAsync(client, DevInboxTestHost.Api + "/push", AbortToken);

        // then
        var smsRow = sms.GetProperty("items")[0];
        smsRow.GetProperty("snippet").GetString().Should().Be("Your code is 123456");
        smsRow.GetProperty("encoding").GetString().Should().Be("gsm7");
        smsRow.GetProperty("parts").GetInt32().Should().Be(1);
        var pushRow = push.GetProperty("items")[0];
        pushRow.GetProperty("isDataOnly").GetBoolean().Should().BeTrue();
        pushRow.GetProperty("dataKeys").EnumerateArray().Select(static k => k.GetString()).Should().Equal("orderId");
        pushRow.TryGetProperty("data", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("/channels")]
    [InlineData("/emails")]
    [InlineData("/sms")]
    [InlineData("/push")]
    public async Task should_forbid_caching_of_every_channel_list(string path)
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);

        // when
        using var response = await DevInboxTestHost.GetAsync(client, DevInboxTestHost.Api + path, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task should_answer_channel_unconfigured_for_a_channel_whose_store_is_not_registered()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken, sms: false, push: false);
        using var client = DevInboxTestHost.Client(app);

        // when
        using var response = await DevInboxTestHost.GetAsync(client, DevInboxTestHost.Api + "/sms", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await DevInboxTestHost.ReadAsync(response, AbortToken);
        problem.GetProperty("code").GetString().Should().Be("dev_inbox:channel_unconfigured");
        problem.GetProperty("detail").GetString().Should().Contain("UseDevelopment()").And.Contain("AddHeadlessSms");
    }

    private static IEnumerable<string?> _Subjects(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(static row => row.GetProperty("subject").GetString());
}
