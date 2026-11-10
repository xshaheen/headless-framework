// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Module;

/// <summary>
/// The Messaging module's own reads refuse bad input in the shape commands answer: a <c>400</c> validation problem
/// naming the refused field in <c>errors</c> and the rule it broke in <c>rules</c>.
/// </summary>
public sealed class MessagingListValidationTests : TestBase
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("/published?status=bogus", "status", "invalid")]
    [InlineData("/received?status=bogus", "status", "invalid")]
    [InlineData("/received?retrying=true&status=failed", "retrying", "invalid")]
    [InlineData("/published?retrying=true&terminal=true", "retrying", "invalid")]
    [InlineData("/published?after=not-a-cursor", "after", "invalid")]
    [InlineData("/received/generations", "messageId", "required")]
    [InlineData("/receipts?targetKind=queue", "targetKind", "invalid")]
    [InlineData("/receipts?operationType=42", "operationType", "invalid")]
    [InlineData("/receipts?after=s1|primary=-", "after", "invalid")]
    public async Task should_answer_a_validation_problem_naming_the_field_when_a_read_gets_bad_input(
        string path,
        string field,
        string rule
    )
    {
        // given
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        using var client = app.GetTestClient();

        // when
        using var response = await client.GetAsync(
            new Uri(MessagingModuleTestHost.Api + path, UriKind.Relative),
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        var problem = document.RootElement;
        problem.GetProperty("errors").GetProperty(field).GetArrayLength().Should().BePositive();
        problem.GetProperty("rules").GetProperty(field).GetString().Should().Be(rule);
        problem.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task should_keep_the_message_of_the_first_refused_check_as_the_detail()
    {
        // given
        await using var app = await MessagingModuleTestHost.StartAsync(_time, AbortToken);
        using var client = app.GetTestClient();

        // when: the retrying conflict is checked before the status, so its message leads
        using var response = await client.GetAsync(
            new Uri($"{MessagingModuleTestHost.Api}/received?retrying=true&status=bogus", UriKind.Relative),
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        document
            .RootElement.GetProperty("detail")
            .GetString()
            .Should()
            .Be("retrying selects failed rows with a retry pending; it takes no status or terminal.");
    }
}
