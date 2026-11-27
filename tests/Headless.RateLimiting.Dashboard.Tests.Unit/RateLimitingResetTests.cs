// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text;
using Headless.Dashboard;
using Headless.RateLimiting;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Tests;

public sealed class RateLimitingResetTests : TestBase
{
    private const string _Reset = RateLimitingHost.Commands + "/rate-limiting.reset";
    private const string _Subject = "someone@example.com";
    private const string _Reason = "Verified by phone";
    private const string _CustomPurpose = "export.request";

    private static readonly AttemptQuota _CustomQuota = new(3, TimeSpan.FromMinutes(10));

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 26, 14, 20, 0, TimeSpan.Zero));
    private readonly RecordingOperatorLog _log = new();

    [Fact]
    public async Task should_reset_a_locked_out_subject_and_log_it_by_purpose_and_fingerprint()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        await _AcquireAsync(app, 5);
        var id = _Id(app, RateLimitingHost.Purpose, _Subject);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await _ResetAsync(client, cookie, id, _Subject, _Reason);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().NotContain(_Subject);
        (await _PeekAsync(app)).Count.Should().Be(0);
        var entry = _log.Entries.Should().ContainSingle().Subject;
        entry.Ids.Should().Equal(id);
        entry.Reason.Should().Be(_Reason);
        entry.Done.Should().Be(1);
        _LogText().Should().NotContain(_Subject);
    }

    [Fact]
    public async Task should_skip_a_subject_with_no_attempts_and_log_one_skipped_reset()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await _ResetAsync(
            client,
            cookie,
            _Id(app, RateLimitingHost.Purpose, _Subject),
            _Subject,
            _Reason
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = (await RateLimitingHost.ReadAsync(response, AbortToken)).GetProperty("rows")[0];
        row.GetProperty("outcome").GetString().Should().BeEquivalentTo("skipped");
        row.GetProperty("reason").GetString().Should().Be("No attempts in the current window");
        _log.Entries.Should().ContainSingle().Which.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task should_refuse_a_subject_that_does_not_match_the_target_fingerprint()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        await _AcquireAsync(app, 5);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await _ResetAsync(
            client,
            cookie,
            _Id(app, RateLimitingHost.Purpose, "someone-else@example.com"),
            _Subject,
            _Reason
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().NotContain(_Subject);
        (await _PeekAsync(app)).Count.Should().Be(5);
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_reason_that_repeats_the_subject_in_any_case()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        await _AcquireAsync(app, 5);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await _ResetAsync(
            client,
            cookie,
            _Id(app, RateLimitingHost.Purpose, _Subject),
            _Subject,
            "Called SOMEONE@EXAMPLE.COM back"
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var text = await response.Content.ReadAsStringAsync(AbortToken);
        text.Should().NotContainEquivalentOf(_Subject);
        text.Should().Contain("reason");
        (await _PeekAsync(app)).Count.Should().Be(5);
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_reset_through_the_fingerprinter_fingerprint_under_a_custom_limiter()
    {
        // given
        var limiter = Substitute.For<IAttemptLimiter>();
        limiter
            .ResetAsync(
                RateLimitingHost.Purpose,
                _Subject,
                RateLimitingHost.FivePerFifteenMinutes,
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        await using var app = await RateLimitingHost.StartAsync(
            _time,
            _log,
            AbortToken,
            configure: services => services.AddSingleton(limiter)
        );
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await _ResetAsync(
            client,
            cookie,
            _Id(app, RateLimitingHost.Purpose, _Subject),
            _Subject,
            _Reason
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _log.Entries.Should().ContainSingle().Which.Done.Should().Be(1);
    }

    [Fact]
    public async Task should_round_trip_a_purpose_that_contains_a_colon()
    {
        // given
        const string purpose = "tenant:acme:sign-in";
        var quota = new AttemptQuota(3, TimeSpan.FromMinutes(10));
        await using var app = await RateLimitingHost.StartAsync(
            _time,
            _log,
            AbortToken,
            limiter: options => options.Purposes[purpose] = quota
        );
        var limiter = app.Services.GetRequiredService<IAttemptLimiter>();
        await limiter.AcquireAsync(purpose, _Subject, quota, AbortToken);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await _ResetAsync(client, cookie, _Id(app, purpose, _Subject), _Subject, _Reason);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await limiter.PeekAsync(purpose, _Subject, quota, AbortToken)).Count.Should().Be(0);
    }

    [Fact]
    public async Task should_refuse_two_targets_in_one_call()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        await _AcquireAsync(app, 5);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);
        var id = _Id(app, RateLimitingHost.Purpose, _Subject);

        // when
        using var response = await RateLimitingHost.PostAsync(
            client,
            _Reset,
            new
            {
                ids = new[] { id, _Id(app, RateLimitingHost.Purpose, "other@example.com") },
                arguments = new { subject = _Subject },
                reason = _Reason,
                confirmed = true,
            },
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _PeekAsync(app)).Count.Should().Be(5);
    }

    [Theory]
    [InlineData(false, true, "reason")]
    [InlineData(true, false, "confirmed")]
    public async Task should_reject_a_reset_without_a_reason_or_a_confirmation(
        bool hasReason,
        bool confirmed,
        string field
    )
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        await _AcquireAsync(app, 5);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await RateLimitingHost.PostAsync(
            client,
            _Reset,
            new
            {
                ids = new[] { _Id(app, RateLimitingHost.Purpose, _Subject) },
                arguments = new { subject = _Subject },
                reason = hasReason ? _Reason : null,
                confirmed,
            },
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RateLimitingHost.ReadAsync(response, AbortToken))
            .GetProperty("errors")
            .TryGetProperty(field, out _)
            .Should()
            .BeTrue();
        (await _PeekAsync(app)).Count.Should().Be(5);
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_declared_purpose_with_a_quota_other_than_the_declared_one()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        await _AcquireAsync(app, 5);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await RateLimitingHost.PostAsync(
            client,
            _Reset,
            new
            {
                ids = new[] { _Id(app, RateLimitingHost.Purpose, _Subject) },
                arguments = new
                {
                    subject = _Subject,
                    limit = 3,
                    windowSeconds = 900,
                },
                reason = _Reason,
                confirmed = true,
            },
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Contain("declared with 5 per 900 seconds");
        (await _PeekAsync(app)).Count.Should().Be(5);
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_an_undeclared_purpose_without_a_quota()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        var limiter = app.Services.GetRequiredService<IAttemptLimiter>();
        await limiter.AcquireAsync(_CustomPurpose, _Subject, _CustomQuota, AbortToken);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await _ResetAsync(client, cookie, _Id(app, _CustomPurpose, _Subject), _Subject, _Reason);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Contain("is not declared");
        (await limiter.PeekAsync(_CustomPurpose, _Subject, _CustomQuota, AbortToken)).Count.Should().Be(1);
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_reset_an_undeclared_purpose_with_the_quota_the_caller_names()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        var limiter = app.Services.GetRequiredService<IAttemptLimiter>();
        await limiter.AcquireAsync(_CustomPurpose, _Subject, _CustomQuota, AbortToken);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await RateLimitingHost.PostAsync(
            client,
            _Reset,
            new
            {
                ids = new[] { _Id(app, _CustomPurpose, _Subject) },
                arguments = new
                {
                    subject = _Subject,
                    limit = _CustomQuota.Limit,
                    windowSeconds = (long)_CustomQuota.Window.TotalSeconds,
                },
                reason = _Reason,
                confirmed = true,
            },
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await limiter.PeekAsync(_CustomPurpose, _Subject, _CustomQuota, AbortToken)).Count.Should().Be(0);
        _log.Entries.Should().ContainSingle().Which.Done.Should().Be(1);
    }

    public static TheoryData<string, HttpStatusCode> SubjectRefusals =>
        new()
        {
            { "{}", HttpStatusCode.BadRequest },
            { """{"subject":42}""", HttpStatusCode.BadRequest },
            { """{"subject":""}""", HttpStatusCode.Conflict },
        };

    [Theory]
    [MemberData(nameof(SubjectRefusals))]
    public async Task should_refuse_a_missing_non_string_or_empty_subject(string arguments, HttpStatusCode status)
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        await _AcquireAsync(app, 5);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);
        var id = _Id(app, RateLimitingHost.Purpose, _Subject);
        using var content = new StringContent(
            $$"""{"ids":["{{id}}"],"arguments":{{arguments}},"reason":"{{_Reason}}","confirmed":true}""",
            Encoding.UTF8,
            "application/json"
        );

        // when
        using var response = await RateLimitingHost.PostAsync(client, _Reset, content, cookie, AbortToken);

        // then
        response.StatusCode.Should().Be(status);
        (await _PeekAsync(app)).Count.Should().Be(5);
        _log.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sign-in.password")]
    [InlineData("sign-in.password:too-short")]
    public async Task should_refuse_a_target_that_is_not_a_purpose_and_a_fingerprint(string id)
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        await _AcquireAsync(app, 5);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);

        // when
        using var response = await _ResetAsync(client, cookie, id, _Subject, _Reason);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Contain("{purpose}:{fingerprint}");
        (await _PeekAsync(app)).Count.Should().Be(5);
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_reset_to_a_viewer_with_a_reason_and_forbid_running_it()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        await _AcquireAsync(app, 5);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "viewer", AbortToken);
        var id = _Id(app, RateLimitingHost.Purpose, _Subject);

        // when
        using var allowed = await RateLimitingHost.GetAsync(
            client,
            $"{RateLimitingHost.Commands}/allowed?kind=rate-limit-subject&id={Uri.EscapeDataString(id)}",
            cookie,
            AbortToken
        );
        using var run = await _ResetAsync(client, cookie, id, _Subject, _Reason);

        // then
        var command = (await RateLimitingHost.ReadAsync(allowed, AbortToken))
            .GetProperty("objects")[0]
            .GetProperty("commands")[0];
        command.GetProperty("allowed").GetBoolean().Should().BeFalse();
        command.GetProperty("disabledReason").GetString().Should().NotBeNullOrWhiteSpace();
        run.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _PeekAsync(app)).Count.Should().Be(5);
    }

    [Fact]
    public async Task should_allow_reset_on_a_subject_for_an_operator()
    {
        // given
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken);
        using var client = RateLimitingHost.Client(app);
        var cookie = await RateLimitingHost.SignInAsync(client, "ops", AbortToken);
        var id = _Id(app, RateLimitingHost.Purpose, _Subject);

        // when
        using var allowed = await RateLimitingHost.GetAsync(
            client,
            $"{RateLimitingHost.Commands}/allowed?kind=rate-limit-subject&id={Uri.EscapeDataString(id)}",
            cookie,
            AbortToken
        );

        // then
        var command = (await RateLimitingHost.ReadAsync(allowed, AbortToken))
            .GetProperty("objects")[0]
            .GetProperty("commands")[0];
        command.GetProperty("id").GetString().Should().Be("rate-limiting.reset");
        command.GetProperty("allowed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task should_answer_a_failed_row_with_an_unknown_outcome_naming_only_the_purpose_when_the_store_fails()
    {
        // given
        var limiter = Substitute.For<IAttemptLimiter>();
        limiter
            .ResetAsync(default!, default!, default!, AbortToken)
            .ReturnsForAnyArgs<ValueTask<bool>>(_ =>
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
        using var response = await _ResetAsync(
            client,
            cookie,
            _Id(app, RateLimitingHost.Purpose, _Subject),
            _Subject,
            _Reason
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = (await RateLimitingHost.ReadAsync(response, AbortToken)).GetProperty("rows")[0];
        row.GetProperty("outcome").GetString().Should().BeEquivalentTo("failed");
        row.GetProperty("reason")
            .GetString()
            .Should()
            .Be(
                "The attempt limiter for 'sign-in.password' failed, so the reset's outcome is unknown: the store may "
                    + "have removed the counter. Look the subject up again before you retry."
            );
        _log.Entries.Should().ContainSingle().Which.Failed.Should().Be(1);
    }

    private static string _Id(WebApplication app, string purpose, string subject) =>
        purpose + ":" + app.Services.GetRequiredService<AttemptSubjectFingerprinter>().Compute(purpose, subject);

    private static async Task _AcquireAsync(WebApplication app, int attempts)
    {
        var limiter = app.Services.GetRequiredService<IAttemptLimiter>();

        for (var i = 0; i < attempts; i++)
        {
            await limiter.AcquireAsync(
                RateLimitingHost.Purpose,
                _Subject,
                RateLimitingHost.FivePerFifteenMinutes,
                AbortToken
            );
        }
    }

    private static ValueTask<AttemptSnapshot> _PeekAsync(WebApplication app) =>
        app
            .Services.GetRequiredService<IAttemptLimiter>()
            .PeekAsync(RateLimitingHost.Purpose, _Subject, RateLimitingHost.FivePerFifteenMinutes, AbortToken);

    private static Task<HttpResponseMessage> _ResetAsync(
        HttpClient client,
        string cookie,
        string id,
        string subject,
        string reason
    ) =>
        RateLimitingHost.PostAsync(
            client,
            _Reset,
            new
            {
                ids = new[] { id },
                arguments = new { subject },
                reason,
                confirmed = true,
            },
            cookie,
            AbortToken
        );

    private string _LogText() =>
        string.Join(
            '\n',
            _log.Entries.Select(static entry => JsonSerializer.Serialize(entry, JsonSerializerOptions.Web))
        );
}
