// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.PushNotifications.Firebase;
using Headless.PushNotifications.Firebase.Internals;
using Headless.Testing.Tests;
using Tests.Fakes;

namespace Tests;

/// <summary>
/// Checks the metrics and spans the Firebase provider emits. The meter is process-wide, so every assertion reads only
/// the measurements carrying this test's own <see cref="FcmTags.Instance"/>, and only the spans under this test's
/// own root activity.
/// </summary>
public sealed class FcmTelemetryTests : TestBase
{
    private static readonly FcmMessage _Message = new()
    {
        Notification = new FcmNotification { Title = "title", Body = "body" },
    };

    private readonly FcmTestRig _rig = new();
    private readonly FcmMetricRecorder _metrics;
    private readonly FcmActivityRecorder _activities = new();

    public FcmTelemetryTests()
    {
        _metrics = new FcmMetricRecorder(_rig.InstanceName);
    }

    protected override ValueTask DisposeAsyncCore()
    {
        _activities.Dispose();
        _metrics.Dispose();
        _rig.Dispose();

        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_count_a_successful_device_send_and_trace_it_without_the_token()
    {
        // when
        await _rig.CreateService().SendAsync("fid-secret-token", _Message, AbortToken);

        // then
        var send = _metrics.Of(FcmMetrics.SendsName).Should().ContainSingle().Subject;
        send.Value.Should().Be(1);
        send.Tag(FcmTags.Outcome).Should().Be("succeeded");
        send.Tag(FcmTags.TargetKind).Should().Be("token");
        send.Tags.Should().NotContainKey(FcmTags.FailureKind).And.NotContainKey(FcmTags.ErrorCode);

        var duration = _metrics.Of(FcmMetrics.SendDurationName).Should().ContainSingle().Subject;
        duration.Tag(FcmTags.Operation).Should().Be("send");

        var activity = _activities.Activities.Should().ContainSingle().Subject;
        activity.OperationName.Should().Be("fcm.send");
        activity.Kind.Should().Be(ActivityKind.Client);
        activity.GetTagItem(FcmTags.Outcome).Should().Be("succeeded");
        activity.GetTagItem(FcmTags.Instance).Should().Be(_rig.InstanceName);
        activity.Status.Should().NotBe(ActivityStatusCode.Error);
        _AllTagValues(activity).Should().NotContain(v => v.Contains("fid-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_tag_a_failed_send_with_its_failure_kind_and_error_code_and_mark_the_span_as_an_error()
    {
        // given
        _rig.Http.Responder = static (_, _) => Task.FromResult(FakeFcmHttpHandler.Error("INVALID_ARGUMENT"));

        // when
        await _rig.CreateService().SendAsync("fid-1", _Message, AbortToken);

        // then
        var send = _metrics.Of(FcmMetrics.SendsName).Should().ContainSingle().Subject;
        send.Tag(FcmTags.Outcome).Should().Be("failed");
        send.Tag(FcmTags.FailureKind).Should().Be("payload");
        send.Tag(FcmTags.ErrorCode).Should().Be("INVALID_ARGUMENT");

        var activity = _activities.Activities.Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(FcmTags.FailureKind).Should().Be("payload");
        activity.GetTagItem(FcmTags.ErrorCode).Should().Be("INVALID_ARGUMENT");
    }

    [Fact]
    public async Task should_report_an_unregistered_token_without_marking_the_span_as_an_error()
    {
        // given
        _rig.Http.Responder = static (_, _) => Task.FromResult(FakeFcmHttpHandler.Error("UNREGISTERED"));

        // when
        await _rig.CreateService().SendAsync("fid-1", _Message, AbortToken);

        // then
        var send = _metrics.Of(FcmMetrics.SendsName).Should().ContainSingle().Subject;
        send.Tag(FcmTags.Outcome).Should().Be("unregistered");
        send.Tag(FcmTags.FailureKind).Should().Be("token_invalid");
        _activities.Activities.Should().ContainSingle().Which.Status.Should().NotBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task should_count_each_in_process_retry_and_one_final_send()
    {
        // given
        _rig.Http.Responder = static (request, _) =>
            Task.FromResult(
                request.Attempt == 1 ? FakeFcmHttpHandler.Error("INTERNAL") : FakeFcmHttpHandler.Success(request.Target)
            );
        var service = _rig.CreateService();

        // when
        var send = service.SendAsync("fid-1", _Message, AbortToken).AsTask();
        _rig.Time.Advance(await _rig.Time.WaitForTimerAsync(1, AbortToken));
        await send.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        var retry = _metrics.Of(FcmMetrics.RetriesName).Should().ContainSingle().Subject;
        retry.Tag(FcmTags.ErrorCode).Should().Be("INTERNAL");
        retry.Tag(FcmTags.FailureKind).Should().Be("server_error");
        _metrics.Of(FcmMetrics.SendsName).Should().ContainSingle().Which.Tag(FcmTags.Outcome).Should().Be("succeeded");
        _metrics.Of(FcmMetrics.SendDurationName).Should().HaveCount(2, "each attempt is one request round");
        _activities.Activities.Should().ContainSingle().Which.GetTagItem(FcmTags.Retry).Should().Be(1);
    }

    [Fact]
    public async Task should_tag_topic_and_condition_sends_by_kind_but_never_by_their_text()
    {
        // given
        var service = _rig.CreateService();

        // when
        await service.SendToTopicAsync("secret-topic", _Message, AbortToken);
        await service.SendToConditionAsync("'secret-topic' in topics", _Message, AbortToken);

        // then
        _metrics
            .Of(FcmMetrics.SendsName)
            .Select(m => m.Tag(FcmTags.TargetKind))
            .Should()
            .BeEquivalentTo(["topic", "condition"]);
        _metrics
            .Of(FcmMetrics.SendsName)
            .SelectMany(m => m.Tags.Values)
            .OfType<string>()
            .Should()
            .NotContain(v => v.Contains("secret", StringComparison.Ordinal));
        _activities.Activities.Should().HaveCount(2);
        _activities
            .Activities.SelectMany(_AllTagValues)
            .Should()
            .NotContain(v => v.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_start_one_multicast_activity_per_round_and_count_every_token_once()
    {
        // given
        _rig.Http.Responder = static (request, _) =>
            Task.FromResult(
                (request.Target, request.Attempt) switch
                {
                    ("fid-internal", 1) => FakeFcmHttpHandler.Error("INTERNAL"),
                    ("fid-invalid", _) => FakeFcmHttpHandler.Error("INVALID_ARGUMENT"),
                    _ => FakeFcmHttpHandler.Success(request.Target),
                }
            );
        var service = _rig.CreateService();

        // when
        var send = service.SendMulticastAsync(["fid-ok", "fid-internal", "fid-invalid"], _Message, AbortToken).AsTask();
        _rig.Time.Advance(await _rig.Time.WaitForTimerAsync(1, AbortToken));
        await send.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then
        var rounds = _activities.Activities.OrderBy(a => a.StartTimeUtc).ToList();
        rounds.Should().HaveCount(2).And.OnlyContain(a => a.OperationName == "fcm.multicast");
        rounds[0].GetTagItem(FcmTags.BatchSize).Should().Be(3);
        rounds[0].GetTagItem(FcmTags.Retry).Should().Be(0);
        rounds[0].GetTagItem(FcmTags.SuccessCount).Should().Be(1);
        rounds[1].GetTagItem(FcmTags.BatchSize).Should().Be(1);
        rounds[1].GetTagItem(FcmTags.Retry).Should().Be(1);
        rounds[1].GetTagItem(FcmTags.SuccessCount).Should().Be(1);

        _metrics
            .Of(FcmMetrics.SendsName)
            .Select(m => m.Tag(FcmTags.Outcome))
            .Should()
            .BeEquivalentTo(["succeeded", "succeeded", "failed"]);
        _metrics.Of(FcmMetrics.RetriesName).Should().ContainSingle();
        _metrics
            .Of(FcmMetrics.SendDurationName)
            .Should()
            .HaveCount(2)
            .And.OnlyContain(m => (string?)m.Tag(FcmTags.Operation) == "multicast");
    }

    private static IEnumerable<string> _AllTagValues(Activity activity)
    {
        return activity.TagObjects.Select(t => t.Value?.ToString() ?? "").Append(activity.DisplayName);
    }
}
