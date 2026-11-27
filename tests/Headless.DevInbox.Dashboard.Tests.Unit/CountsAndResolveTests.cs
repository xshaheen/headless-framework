// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>The sidebar counts and the Go to id lookup the shell asks every module for.</summary>
public sealed class CountsAndResolveTests : TestBase
{
    [Fact]
    public async Task should_count_each_configured_channel_and_leave_out_the_unregistered_ones()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken, sms: false, push: false);
        using var client = DevInboxTestHost.Client(app);
        await DevInboxTestHost.SendEmailAsync(app, "one", AbortToken);
        await DevInboxTestHost.SendEmailAsync(app, "two", AbortToken);

        // when
        var answer = await DevInboxTestHost.GetJsonAsync(client, "/headless/api/counts?product=dev-inbox", AbortToken);

        // then
        var counts = answer.GetProperty("counts");
        counts.GetProperty("emails").GetProperty("count").GetInt64().Should().Be(2);
        counts.GetProperty("emails").GetProperty("capped").GetBoolean().Should().BeFalse();
        counts.TryGetProperty("sms", out _).Should().BeFalse();
        counts.TryGetProperty("push", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_resolve_a_captured_push_by_its_id_with_its_title()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        var push = await DevInboxTestHost.SendPushAsync(
            app,
            new PushNotificationRequest { Title = "Hello", Body = "World" },
            AbortToken
        );

        // when
        using var response = await DevInboxTestHost.PostAsync(
            client,
            "/headless/api/resolve",
            new { query = push.Id.ToString("D") },
            AbortToken
        );
        var resolved = await DevInboxTestHost.ReadAsync(response, AbortToken);

        // then
        var item = _Items(resolved).Should().ContainSingle().Subject;
        item.GetProperty("target").GetProperty("kind").GetString().Should().Be("push");
        item.GetProperty("target").GetProperty("id").GetString().Should().Be(push.Id.ToString("D"));
        item.GetProperty("title").GetString().Should().Be("Hello");
    }

    [Fact]
    public async Task should_resolve_nothing_for_an_unknown_guid()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        await DevInboxTestHost.SendPushAsync(
            app,
            new PushNotificationRequest { Title = "Hello", Body = "World" },
            AbortToken
        );

        // when
        using var response = await DevInboxTestHost.PostAsync(
            client,
            "/headless/api/resolve",
            new { query = Guid.NewGuid().ToString("D") },
            AbortToken
        );
        var resolved = await DevInboxTestHost.ReadAsync(response, AbortToken);

        // then
        _Items(resolved).Should().BeEmpty();
    }

    private static IEnumerable<JsonElement> _Items(JsonElement resolved) =>
        resolved
            .GetProperty("modules")
            .EnumerateArray()
            .Where(static module =>
                string.Equals(module.GetProperty("id").GetString(), "dev-inbox", StringComparison.Ordinal)
            )
            .SelectMany(static module => module.GetProperty("items").EnumerateArray());
}
