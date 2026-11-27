// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text;
using Headless.RateLimiting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Tests;

public sealed class RateLimitingLookupTests : TestBase
{
    private const string _Lookup = RateLimitingHost.Api + "/lookup";
    private const string _Subject = "someone@example.com";

    // Five minutes into a quarter-hour-aligned window: it runs 14:15 to 14:30 and closes in ten minutes.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 26, 14, 20, 0, TimeSpan.Zero));
    private readonly RecordingOperatorLog _log = new();

    [Fact]
    public async Task should_read_a_locked_out_subject_with_its_window_and_fingerprint()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        var limiter = app.Services.GetRequiredService<IAttemptLimiter>();

        for (var i = 0; i < 5; i++)
        {
            await limiter.AcquireAsync(
                RateLimitingHost.Purpose,
                _Subject,
                RateLimitingHost.FivePerFifteenMinutes,
                AbortToken
            );
        }

        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await RateLimitingHost.PostAsync(
            client,
            _Lookup,
            new { purpose = RateLimitingHost.Purpose, subject = _Subject },
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync(AbortToken);
        text.Should().NotContain(_Subject);
        var body = JsonDocument.Parse(text).RootElement;
        body.GetProperty("purpose").GetString().Should().Be(RateLimitingHost.Purpose);
        body.GetProperty("count").GetInt64().Should().Be(5);
        body.GetProperty("limit").GetInt32().Should().Be(5);
        body.GetProperty("remaining").GetInt64().Should().Be(0);
        body.GetProperty("isLockedOut").GetBoolean().Should().BeTrue();
        body.GetProperty("windowStartsAt")
            .GetDateTimeOffset()
            .Should()
            .Be(new DateTimeOffset(2026, 11, 26, 14, 15, 0, TimeSpan.Zero));
        body.GetProperty("windowEndsAt")
            .GetDateTimeOffset()
            .Should()
            .Be(new DateTimeOffset(2026, 11, 26, 14, 30, 0, TimeSpan.Zero));
        body.GetProperty("closesInSeconds").GetInt64().Should().Be(600);
        body.GetProperty("declared").GetBoolean().Should().BeTrue();
        body.GetProperty("quota").GetProperty("windowSeconds").GetInt64().Should().Be(900);
        body.GetProperty("fingerprint")
            .GetString()
            .Should()
            .Be(
                app.Services.GetRequiredService<AttemptSubjectFingerprinter>()
                    .Compute(RateLimitingHost.Purpose, _Subject)
            );
    }

    [Fact]
    public async Task should_read_a_subject_with_no_attempts_as_zero_and_create_no_counter()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "viewer", AbortToken);
        var lookup = new { purpose = RateLimitingHost.Purpose, subject = _Subject };

        // when
        using var first = await RateLimitingHost.PostAsync(client, _Lookup, lookup, cookie, AbortToken);
        using var second = await RateLimitingHost.PostAsync(client, _Lookup, lookup, cookie, AbortToken);

        // then
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await RateLimitingHost.ReadAsync(second, AbortToken);
        body.GetProperty("count").GetInt64().Should().Be(0);
        body.GetProperty("remaining").GetInt64().Should().Be(5);
        body.GetProperty("isLockedOut").GetBoolean().Should().BeFalse();
        var acquired = await app
            .Services.GetRequiredService<IAttemptLimiter>()
            .AcquireAsync(RateLimitingHost.Purpose, _Subject, RateLimitingHost.FivePerFifteenMinutes, AbortToken);
        acquired.Count.Should().Be(1);
    }

    [Fact]
    public async Task should_serve_the_fingerprinter_fingerprint_when_a_custom_limiter_reports_another()
    {
        // given
        var limiter = Substitute.For<IAttemptLimiter>();
        limiter
            .PeekAsync(
                RateLimitingHost.Purpose,
                _Subject,
                RateLimitingHost.FivePerFifteenMinutes,
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new AttemptSnapshot(
                    RateLimitingHost.Purpose,
                    "custom-fingerprint",
                    2,
                    5,
                    new DateTimeOffset(2026, 11, 26, 14, 15, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 11, 26, 14, 30, 0, TimeSpan.Zero),
                    TimeSpan.FromMinutes(10)
                )
            );
        await using var app = await RateLimitingHost.StartAsync(
            _time,
            _log,
            AbortToken,
            configure: services => services.AddSingleton(limiter)
        );
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await RateLimitingHost.PostAsync(
            client,
            _Lookup,
            new { purpose = RateLimitingHost.Purpose, subject = _Subject },
            cookie,
            AbortToken
        );

        // then
        var body = await RateLimitingHost.ReadAsync(response, AbortToken);
        body.GetProperty("count").GetInt64().Should().Be(2);
        body.GetProperty("fingerprint")
            .GetString()
            .Should()
            .Be(
                app.Services.GetRequiredService<AttemptSubjectFingerprinter>()
                    .Compute(RateLimitingHost.Purpose, _Subject)
            );
    }

    [Fact]
    public async Task should_read_an_undeclared_purpose_with_the_quota_the_caller_names()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        var quota = new AttemptQuota(3, TimeSpan.FromMinutes(10));
        await app
            .Services.GetRequiredService<IAttemptLimiter>()
            .AcquireAsync("otp.verify", _Subject, quota, AbortToken);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await RateLimitingHost.PostAsync(
            client,
            _Lookup,
            new
            {
                purpose = "otp.verify",
                subject = _Subject,
                quota = new { limit = 3, windowSeconds = 600 },
            },
            cookie,
            AbortToken
        );

        // then
        var body = await RateLimitingHost.ReadAsync(response, AbortToken);
        body.GetProperty("count").GetInt64().Should().Be(1);
        body.GetProperty("limit").GetInt32().Should().Be(3);
        body.GetProperty("declared").GetBoolean().Should().BeFalse();
    }

    public static TheoryData<string> QuotaRefusals =>
        [
            """{"purpose":"otp.verify","subject":"someone@example.com"}""",
            """{"purpose":"otp.verify","subject":"someone@example.com","quota":{"limit":0,"windowSeconds":600}}""",
            """{"purpose":"sign-in.password","subject":"someone@example.com","quota":{"limit":5,"windowSeconds":60}}""",
        ];

    [Theory]
    [MemberData(nameof(QuotaRefusals))]
    public async Task should_refuse_a_missing_invalid_or_different_quota_naming_the_quota_field(string json)
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await _PostRawAsync(client, json, cookie);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync(AbortToken);
        text.Should().NotContain(_Subject);
        JsonDocument.Parse(text).RootElement.GetProperty("errors").TryGetProperty("quota", out _).Should().BeTrue();
    }

    public static TheoryData<string, string> FieldRefusals =>
        new()
        {
            { """{"purpose":"sign-in.password","subject":""}""", "subject" },
            { """{"purpose":"","subject":"someone@example.com"}""", "purpose" },
            {
                $$"""{"purpose":"sign-in.password","subject":"someone@example.com{{new string('x', 512)}}"}""",
                "subject"
            },
            { """{"purpose":"sign-in.password","subject":"someone@example.com""", "body" },
        };

    [Theory]
    [MemberData(nameof(FieldRefusals))]
    public async Task should_refuse_a_bad_body_naming_the_field_and_never_the_subject(string json, string field)
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await _PostRawAsync(client, json, cookie);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync(AbortToken);
        text.Should().NotContain(_Subject);
        JsonDocument.Parse(text).RootElement.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Fact]
    public async Task should_answer_503_naming_only_the_purpose_when_the_counter_store_fails()
    {
        // given
        var limiter = Substitute.For<IAttemptLimiter>();
        limiter
            .PeekAsync(default!, default!, default!, AbortToken)
            .ReturnsForAnyArgs<ValueTask<AttemptSnapshot>>(_ =>
                throw new AttemptLimiterUnavailableException(RateLimitingHost.Purpose, new TimeoutException())
            );
        await using var app = await RateLimitingHost.StartAsync(
            _time,
            _log,
            AbortToken,
            configure: services => services.AddSingleton(limiter)
        );
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await RateLimitingHost.PostAsync(
            client,
            _Lookup,
            new { purpose = RateLimitingHost.Purpose, subject = _Subject },
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var text = await response.Content.ReadAsStringAsync(AbortToken);
        text.Should().NotContain(_Subject);
        JsonDocument
            .Parse(text)
            .RootElement.GetProperty("detail")
            .GetString()
            .Should()
            .Be("The attempt limiter for 'sign-in.password' is unavailable.");
    }

    [Fact]
    public async Task should_refuse_a_lookup_without_a_session()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        using var client = RateLimitingHost.Client(app);

        // when
        using var response = await RateLimitingHost.PostAsync(
            client,
            _Lookup,
            new { purpose = RateLimitingHost.Purpose, subject = _Subject },
            cookie: null,
            AbortToken
        );

        // then
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    private static Task<HttpResponseMessage> _PostRawAsync(HttpClient client, string json, string cookie) =>
        RateLimitingHost.PostAsync(
            client,
            _Lookup,
            new StringContent(json, Encoding.UTF8, "application/json"),
            cookie,
            AbortToken
        );
}
