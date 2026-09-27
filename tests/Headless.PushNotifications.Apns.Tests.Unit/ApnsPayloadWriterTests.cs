// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.PushNotifications.Apns;
using Headless.PushNotifications.Apns.Internals;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class ApnsPayloadWriterTests : TestBase
{
    private const string _BundleId = "com.example.app";

    // 2026-09-25T10:00:00Z; every epoch-second literal below is derived from this instant.
    private static readonly DateTimeOffset _Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(_Now);

    #region Alert

    [Fact]
    public void should_write_every_aps_control_when_alert_sets_them_all()
    {
        // given
        var notification = new ApnsAlertNotification
        {
            Alert = new ApnsAlert
            {
                Title = "Title",
                Subtitle = "Sub",
                Body = "Body",
            },
            Badge = 0,
            Sound = ApnsSound.Named("chime"),
            ThreadId = "thread-1",
            Category = "REPLY",
            MutableContent = true,
            InterruptionLevel = ApnsInterruptionLevel.TimeSensitive,
            RelevanceScore = 0.5,
            TargetContentId = "window-1",
            Data = new JsonObject { ["k"] = "v" },
        };

        // when
        var prepared = _Prepare(notification);

        // then
        _Json(prepared)
            .Should()
            .Be(
                """{"aps":{"alert":{"title":"Title","subtitle":"Sub","body":"Body"},"badge":0,"sound":"chime","thread-id":"thread-1","category":"REPLY","mutable-content":1,"interruption-level":"time-sensitive","relevance-score":0.5,"target-content-id":"window-1"},"k":"v"}"""
            );
        _Lines(prepared.Headers)
            .Should()
            .Equal("apns-push-type: alert", $"apns-topic: {_BundleId}", "apns-priority: 10");
    }

    [Fact]
    public void should_write_localization_keys_instead_of_literals_when_alert_is_localized()
    {
        // given
        var notification = new ApnsAlertNotification
        {
            Alert = new ApnsAlert
            {
                TitleLocKey = "GAME_TITLE",
                TitleLocArgs = ["Shelly"],
                SubtitleLocKey = "GAME_SUB",
                SubtitleLocArgs = ["2", "3"],
                LocKey = "GAME_BODY",
                LocArgs = ["Jenna"],
                LaunchImage = "launch.png",
            },
        };

        // when
        var json = _Json(_Prepare(notification));

        // then
        json.Should()
            .Be(
                """{"aps":{"alert":{"title-loc-key":"GAME_TITLE","title-loc-args":["Shelly"],"subtitle-loc-key":"GAME_SUB","subtitle-loc-args":["2","3"],"loc-key":"GAME_BODY","loc-args":["Jenna"],"launch-image":"launch.png"}}}"""
            );
    }

    [Fact]
    public void should_throw_when_alert_sets_both_title_and_its_localization_key()
    {
        // given
        var notification = new ApnsAlertNotification
        {
            Alert = new ApnsAlert { Title = "Title", TitleLocKey = "KEY" },
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_alert_sets_localization_args_without_the_key()
    {
        // given
        var notification = new ApnsAlertNotification
        {
            Alert = new ApnsAlert { Body = "Body", LocArgs = ["x"] },
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_write_critical_sound_dictionary_when_sound_is_critical()
    {
        // given
        var notification = new ApnsAlertNotification
        {
            Alert = new ApnsAlert { Body = "Fire" },
            Sound = ApnsSound.Critical("alarm", 0.8),
        };

        // when
        var json = _Json(_Prepare(notification));

        // then
        json.Should().Be("""{"aps":{"alert":{"body":"Fire"},"sound":{"critical":1,"name":"alarm","volume":0.8}}}""");
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    public void should_throw_when_critical_sound_volume_is_outside_zero_to_one(double volume)
    {
        // when
        var act = () => ApnsSound.Critical("alarm", volume);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(1.2)]
    [InlineData(-0.5)]
    public void should_throw_when_alert_relevance_score_is_outside_zero_to_one(double score)
    {
        // given
        var notification = new ApnsAlertNotification
        {
            Alert = new ApnsAlert { Body = "Body" },
            RelevanceScore = score,
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_alert_badge_is_negative()
    {
        // given
        var notification = new ApnsAlertNotification { Badge = -1 };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_alert_has_no_alert_badge_or_sound()
    {
        // given
        var notification = new ApnsAlertNotification { Category = "REPLY" };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_the_alert_has_no_title_subtitle_or_body()
    {
        // given
        var notification = new ApnsAlertNotification { Alert = new ApnsAlert() };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("An APNs alert needs a title, a subtitle, or a body.*");
    }

    [Fact]
    public void should_write_badge_only_payload_when_alert_sets_only_a_badge()
    {
        // given
        var notification = new ApnsAlertNotification { Badge = 7 };

        // when
        var json = _Json(_Prepare(notification));

        // then
        json.Should().Be("""{"aps":{"badge":7}}""");
    }

    [Fact]
    public void should_use_the_message_priority_over_the_options_when_alert_sets_one()
    {
        // given
        var notification = new ApnsAlertNotification
        {
            Alert = new ApnsAlert { Body = "Body" },
            Priority = ApnsPriority.PowerPrioritized,
        };

        // when
        var headers = _Prepare(notification).Headers;

        // then
        headers.Priority.Should().Be(ApnsPriority.PowerPrioritized);
        _Lines(headers).Should().Contain("apns-priority: 1");
    }

    [Fact]
    public void should_use_the_options_priority_when_alert_sets_none()
    {
        // given
        var notification = new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Body" } };

        // when
        var headers = _Prepare(notification, o => o.Priority = ApnsPriority.PowerConsiderate).Headers;

        // then
        headers.Priority.Should().Be(ApnsPriority.PowerConsiderate);
    }

    [Fact]
    public void should_send_as_voip_to_the_voip_topic_when_the_instance_is_voip()
    {
        // given
        var notification = new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Call" } };

        // when
        var headers = _Prepare(notification, o => o.PushType = ApnsPushType.Voip).Headers;

        // then
        _Lines(headers)
            .Should()
            .Equal("apns-push-type: voip", $"apns-topic: {_BundleId}.voip", "apns-priority: 10", "apns-expiration: 0");
    }

    [Fact]
    public void should_default_every_voip_push_to_deliver_once()
    {
        // given
        var alert = new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Call" } };
        var data = new ApnsVoipDataNotification { Data = new JsonObject { ["callId"] = "1" } };
        var raw = new ApnsRawNotification { Type = ApnsNotificationType.Voip, Payload = _Element("""{"aps":{}}""") };
        ApnsNotification[] notifications = [alert, data, raw];

        // when
        var headers = notifications.Select(n => _Prepare(n, o => o.PushType = ApnsPushType.Voip).Headers);

        // then
        headers.Should().AllSatisfy(h => _Lines(h).Should().Contain("apns-expiration: 0"));
    }

    [Fact]
    public void should_send_the_caller_expiration_when_a_voip_push_sets_one()
    {
        // given
        var notification = new ApnsAlertNotification
        {
            Alert = new ApnsAlert { Body = "Call" },
            Expiration = ApnsExpiration.At(_Now.AddSeconds(30)),
        };

        // when
        var headers = _Prepare(notification, o => o.PushType = ApnsPushType.Voip).Headers;

        // then
        _Lines(headers)
            .Should()
            .Contain(
                "apns-expiration: " + _Now.AddSeconds(30).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
            );
    }

    #endregion

    #region Background

    [Fact]
    public void should_write_content_available_with_top_level_data_when_notification_is_background()
    {
        // given
        var notification = new ApnsBackgroundNotification { Data = new JsonObject { ["sync"] = "1" } };

        // when
        var prepared = _Prepare(notification, o => o.Priority = ApnsPriority.Immediate);

        // then
        _Json(prepared).Should().Be("""{"aps":{"content-available":1},"sync":"1"}""");
        _Lines(prepared.Headers)
            .Should()
            .Equal("apns-push-type: background", $"apns-topic: {_BundleId}", "apns-priority: 5");
    }

    [Fact]
    public void should_write_bare_content_available_when_background_has_no_data()
    {
        // when
        var json = _Json(_Prepare(new ApnsBackgroundNotification()));

        // then
        json.Should().Be("""{"aps":{"content-available":1}}""");
    }

    [Fact]
    public void should_throw_when_background_is_sent_through_a_voip_instance()
    {
        // when
        var act = () => _Prepare(new ApnsBackgroundNotification(), o => o.PushType = ApnsPushType.Voip);

        // then
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Live Activity

    [Fact]
    public void should_write_the_update_event_to_the_live_activity_topic_when_update_has_a_stale_date()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Update,
            ContentState = _Element("""{"score":2}"""),
            StaleDate = _Now.AddMinutes(10),
        };

        // when
        var prepared = _Prepare(notification);

        // then
        _Json(prepared)
            .Should()
            .Be(
                """{"aps":{"timestamp":1790330400,"event":"update","content-state":{"score":2},"stale-date":1790331000}}"""
            );
        _Lines(prepared.Headers)
            .Should()
            .Equal(
                "apns-push-type: liveactivity",
                $"apns-topic: {_BundleId}.push-type.liveactivity",
                "apns-priority: 5"
            );
    }

    [Fact]
    public void should_write_the_caller_timestamp_when_live_activity_sets_one()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Update,
            ContentState = _Element("""{"score":2}"""),
            Timestamp = _Now.AddHours(2),
        };

        // when
        var json = _Json(_Prepare(notification));

        // then
        json.Should().Be("""{"aps":{"timestamp":1790337600,"event":"update","content-state":{"score":2}}}""");
    }

    [Fact]
    public void should_write_attributes_and_alert_when_live_activity_starts()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Start,
            ContentState = _Element("""{"score":0}"""),
            AttributesType = "MatchAttributes",
            Attributes = _Element("""{"home":"A","away":"B"}"""),
            Alert = new ApnsAlert { Title = "Kickoff", Body = "A vs B" },
            RelevanceScore = 100,
            Priority = ApnsPriority.Immediate,
        };

        // when
        var prepared = _Prepare(notification);

        // then
        _Json(prepared)
            .Should()
            .Be(
                """{"aps":{"timestamp":1790330400,"event":"start","content-state":{"score":0},"relevance-score":100,"attributes-type":"MatchAttributes","attributes":{"home":"A","away":"B"},"alert":{"title":"Kickoff","body":"A vs B"}}}"""
            );
        prepared.Headers.Priority.Should().Be(ApnsPriority.Immediate);
    }

    [Fact]
    public void should_write_localized_live_activity_alert_as_loc_dictionaries()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Update,
            ContentState = _Element("""{"score":1}"""),
            Alert = new ApnsAlert
            {
                TitleLocKey = "GOAL_TITLE",
                TitleLocArgs = ["A"],
                LocKey = "GOAL_BODY",
            },
        };

        // when
        var json = _Json(_Prepare(notification));

        // then
        json.Should()
            .Be(
                """{"aps":{"timestamp":1790330400,"event":"update","content-state":{"score":1},"alert":{"title":{"loc-key":"GOAL_TITLE","loc-args":["A"]},"body":{"loc-key":"GOAL_BODY"}}}}"""
            );
    }

    [Fact]
    public void should_write_the_sound_inside_the_alert_when_live_activity_sets_one()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Update,
            ContentState = _Element("""{"score":1}"""),
            Alert = new ApnsAlert { Title = "Goal", Body = "A scores" },
            Sound = ApnsSound.Named("chime.aiff"),
        };

        // when
        var json = _Json(_Prepare(notification));

        // then
        json.Should()
            .Be(
                """{"aps":{"timestamp":1790330400,"event":"update","content-state":{"score":1},"alert":{"title":"Goal","body":"A scores","sound":"chime.aiff"}}}"""
            );
    }

    [Fact]
    public void should_throw_when_live_activity_sets_a_sound_without_an_alert()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Update,
            ContentState = _Element("""{"score":1}"""),
            Sound = ApnsSound.Default,
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_live_activity_sound_is_critical()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Update,
            ContentState = _Element("""{"score":1}"""),
            Alert = new ApnsAlert { Body = "A scores" },
            Sound = ApnsSound.Critical("default", 0.5),
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_live_activity_alert_sets_a_field_live_activities_do_not_show()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Update,
            ContentState = _Element("""{"score":1}"""),
            Alert = new ApnsAlert { Title = "T", Subtitle = "S" },
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_live_activity_start_has_no_attributes()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Start,
            ContentState = _Element("""{"score":0}"""),
            AttributesType = "MatchAttributes",
            Alert = new ApnsAlert { Title = "Kickoff" },
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_live_activity_start_has_no_attributes_type()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Start,
            ContentState = _Element("""{"score":0}"""),
            Attributes = _Element("""{"home":"A"}"""),
            Alert = new ApnsAlert { Title = "Kickoff" },
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_live_activity_start_has_no_alert()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Start,
            ContentState = _Element("""{"score":0}"""),
            AttributesType = "MatchAttributes",
            Attributes = _Element("""{"home":"A"}"""),
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_live_activity_update_sets_attributes()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Update,
            ContentState = _Element("""{"score":0}"""),
            AttributesType = "MatchAttributes",
            Attributes = _Element("""{"home":"A"}"""),
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(ApnsLiveActivityEvent.Start)]
    [InlineData(ApnsLiveActivityEvent.Update)]
    public void should_throw_when_live_activity_start_or_update_has_no_content_state(
        ApnsLiveActivityEvent activityEvent
    )
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = activityEvent,
            AttributesType = "MatchAttributes",
            Attributes = _Element("""{"home":"A"}"""),
            Alert = new ApnsAlert { Title = "Kickoff" },
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_write_the_dismissal_date_when_live_activity_ends_without_content_state()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.End,
            DismissalDate = _Now.AddDays(1),
        };

        // when
        var json = _Json(_Prepare(notification));

        // then
        json.Should().Be("""{"aps":{"timestamp":1790330400,"event":"end","dismissal-date":1790416800}}""");
    }

    [Fact]
    public void should_throw_when_live_activity_content_state_is_not_a_json_object()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Update,
            ContentState = _Element("[1,2]"),
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_live_activity_attributes_is_not_a_json_object()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Start,
            ContentState = _Element("""{"score":0}"""),
            AttributesType = "MatchAttributes",
            Attributes = _Element("\"home\""),
            Alert = new ApnsAlert { Title = "Kickoff" },
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_live_activity_priority_is_power_prioritized()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.End,
            Priority = ApnsPriority.PowerPrioritized,
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_live_activity_is_sent_through_a_voip_instance()
    {
        // given
        var notification = new ApnsLiveActivityNotification { Event = ApnsLiveActivityEvent.End };

        // when
        var act = () => _Prepare(notification, o => o.PushType = ApnsPushType.Voip);

        // then
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Niche push types

    public static TheoryData<string, ApnsNotification, string, string[]> NichePushTypes =>
        new()
        {
            {
                "location",
                new ApnsLocationNotification { Data = _KeyValue() },
                """{"aps":{},"k":"v"}""",
                ["apns-push-type: location", $"apns-topic: {_BundleId}.location-query", "apns-priority: 10"]
            },
            {
                "push-to-talk",
                new ApnsPushToTalkNotification { Data = _KeyValue() },
                """{"aps":{},"k":"v"}""",
                [
                    "apns-push-type: pushtotalk",
                    $"apns-topic: {_BundleId}.voip-ptt",
                    "apns-priority: 10",
                    "apns-expiration: 0",
                ]
            },
            {
                "widgets",
                new ApnsWidgetsNotification(),
                """{"aps":{"content-changed":true}}""",
                ["apns-push-type: widgets", $"apns-topic: {_BundleId}.push-type.widgets", "apns-priority: 10"]
            },
            {
                "controls",
                new ApnsControlsNotification(),
                """{"aps":{"content-changed":true}}""",
                ["apns-push-type: controls", $"apns-topic: {_BundleId}.push-type.controls", "apns-priority: 10"]
            },
            {
                "complication",
                new ApnsComplicationNotification { Data = _KeyValue() },
                """{"aps":{},"k":"v"}""",
                ["apns-push-type: complication", $"apns-topic: {_BundleId}.complication", "apns-priority: 10"]
            },
            {
                "file provider",
                new ApnsFileProviderNotification { ContainerIdentifier = "c", Domain = "d" },
                """{"container-identifier":"c","domain":"d"}""",
                ["apns-push-type: fileprovider", $"apns-topic: {_BundleId}.pushkit.fileprovider", "apns-priority: 10"]
            },
        };

    [Theory]
    [MemberData(nameof(NichePushTypes))]
    public void should_write_the_exact_headers_and_payload_of_each_niche_push_type(
        string scenario,
        ApnsNotification notification,
        string expectedJson,
        string[] expectedHeaders
    )
    {
        // when: the options priority must not leak into push types whose default Apple fixes.
        var prepared = _Prepare(notification, o => o.Priority = ApnsPriority.PowerConsiderate);

        // then
        _Json(prepared).Should().Be(expectedJson, scenario);
        _Lines(prepared.Headers).Should().Equal(expectedHeaders, scenario);
    }

    [Fact]
    public void should_write_a_bare_empty_aps_when_location_has_no_data()
    {
        // when
        var json = _Json(_Prepare(new ApnsLocationNotification()));

        // then
        json.Should().Be("""{"aps":{}}""");
    }

    [Fact]
    public void should_write_only_content_changed_when_widgets_or_controls_are_sent()
    {
        // when
        var widgets = _Json(_Prepare(new ApnsWidgetsNotification { CollapseId = "w" }));
        var controls = _Json(_Prepare(new ApnsControlsNotification { CollapseId = "c" }));

        // then
        widgets.Should().Be("""{"aps":{"content-changed":true}}""");
        controls.Should().Be("""{"aps":{"content-changed":true}}""");
    }

    [Fact]
    public void should_send_the_explicit_expiration_when_push_to_talk_sets_one()
    {
        // given
        var notification = new ApnsPushToTalkNotification { Expiration = ApnsExpiration.At(_Now.AddHours(2)) };

        // when
        var headers = _Prepare(notification).Headers;

        // then
        _Lines(headers).Should().Contain("apns-expiration: 1790337600").And.NotContain("apns-expiration: 0");
    }

    public static TheoryData<string, ApnsNotification> NicheTypesWithPriority5 =>
        new()
        {
            {
                "location",
                new ApnsLocationNotification { Priority = ApnsPriority.PowerConsiderate }
            },
            {
                "widgets",
                new ApnsWidgetsNotification { Priority = ApnsPriority.PowerConsiderate }
            },
            {
                "controls",
                new ApnsControlsNotification { Priority = ApnsPriority.PowerConsiderate }
            },
            {
                "complication",
                new ApnsComplicationNotification { Priority = ApnsPriority.PowerConsiderate }
            },
            {
                "file provider",
                new ApnsFileProviderNotification
                {
                    ContainerIdentifier = "c",
                    Domain = "d",
                    Priority = ApnsPriority.PowerConsiderate,
                }
            },
        };

    [Theory]
    [MemberData(nameof(NicheTypesWithPriority5))]
    public void should_send_priority_5_when_a_niche_type_asks_for_it(string scenario, ApnsNotification notification)
    {
        // when
        var headers = _Prepare(notification).Headers;

        // then
        _Lines(headers).Should().Contain("apns-priority: 5", scenario);
    }

    public static TheoryData<string, ApnsNotification> NicheTypesWithPriority1 =>
        new()
        {
            {
                "location",
                new ApnsLocationNotification { Priority = ApnsPriority.PowerPrioritized }
            },
            {
                "widgets",
                new ApnsWidgetsNotification { Priority = ApnsPriority.PowerPrioritized }
            },
            {
                "controls",
                new ApnsControlsNotification { Priority = ApnsPriority.PowerPrioritized }
            },
            {
                "complication",
                new ApnsComplicationNotification { Priority = ApnsPriority.PowerPrioritized }
            },
            {
                "file provider",
                new ApnsFileProviderNotification
                {
                    ContainerIdentifier = "c",
                    Domain = "d",
                    Priority = ApnsPriority.PowerPrioritized,
                }
            },
        };

    [Theory]
    [MemberData(nameof(NicheTypesWithPriority1))]
    public void should_throw_when_a_niche_type_uses_priority_1(string scenario, ApnsNotification notification)
    {
        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>(scenario);
    }

    [Theory]
    [InlineData("", "d")]
    [InlineData(" ", "d")]
    [InlineData("c", "")]
    [InlineData("c", " ")]
    public void should_throw_when_file_provider_container_identifier_or_domain_is_blank(
        string containerIdentifier,
        string domain
    )
    {
        // given
        var notification = new ApnsFileProviderNotification
        {
            ContainerIdentifier = containerIdentifier,
            Domain = domain,
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    public static TheoryData<string, ApnsNotification> NicheTypesWithReservedKey =>
        new()
        {
            {
                "location",
                new ApnsLocationNotification { Data = _Reserved() }
            },
            {
                "push-to-talk",
                new ApnsPushToTalkNotification { Data = _Reserved() }
            },
            {
                "complication",
                new ApnsComplicationNotification { Data = _Reserved() }
            },
        };

    [Theory]
    [MemberData(nameof(NicheTypesWithReservedKey))]
    public void should_throw_when_niche_data_uses_the_reserved_aps_key(string scenario, ApnsNotification notification)
    {
        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>(scenario);
    }

    public static TheoryData<string, Func<int, ApnsNotification>> NicheTypesWithData =>
        new()
        {
            {
                "location",
                filler => new ApnsLocationNotification { Data = _Filler(filler) }
            },
            {
                "push-to-talk",
                filler => new ApnsPushToTalkNotification { Data = _Filler(filler) }
            },
            {
                "complication",
                filler => new ApnsComplicationNotification { Data = _Filler(filler) }
            },
        };

    [Theory]
    [MemberData(nameof(NicheTypesWithData))]
    public void should_accept_4096_bytes_and_refuse_one_more_when_a_niche_type_carries_data(
        string scenario,
        Func<int, ApnsNotification> create
    )
    {
        // given: {"aps":{},"k":"<filler>"} is 17 bytes of fixed JSON plus the filler.
        var atLimit = create(4096 - 17);
        var overLimit = create(4096 - 17 + 1);

        // when
        var accepted = _Prepare(atLimit);
        var act = () => _Prepare(overLimit);

        // then
        accepted.Payload.Should().HaveCount(4096, scenario);
        act.Should().Throw<ArgumentException>(scenario);
    }

    [Fact]
    public void should_refuse_a_file_provider_payload_over_4096_bytes()
    {
        // given: {"container-identifier":"<filler>","domain":"d"} is 40 bytes of fixed JSON plus the filler.
        var atLimit = new ApnsFileProviderNotification
        {
            ContainerIdentifier = new string('x', 4096 - 40),
            Domain = "d",
        };
        var overLimit = atLimit with { ContainerIdentifier = new string('x', 4096 - 40 + 1) };

        // when
        var accepted = _Prepare(atLimit);
        var act = () => _Prepare(overLimit);

        // then
        accepted.Payload.Should().HaveCount(4096);
        act.Should().Throw<ArgumentException>();
    }

    public static TheoryData<string, ApnsNotification> NicheNotifications =>
        new()
        {
            { "location", new ApnsLocationNotification() },
            { "push-to-talk", new ApnsPushToTalkNotification() },
            { "widgets", new ApnsWidgetsNotification() },
            { "controls", new ApnsControlsNotification() },
            { "complication", new ApnsComplicationNotification() },
            {
                "file provider",
                new ApnsFileProviderNotification { ContainerIdentifier = "c", Domain = "d" }
            },
        };

    [Theory]
    [MemberData(nameof(NicheNotifications))]
    public void should_throw_when_a_niche_type_is_sent_through_a_voip_instance(
        string scenario,
        ApnsNotification notification
    )
    {
        // when
        var act = () => _Prepare(notification, o => o.PushType = ApnsPushType.Voip);

        // then
        act.Should().Throw<ArgumentException>(scenario);
    }

    private static JsonObject _KeyValue() => new() { ["k"] = "v" };

    private static JsonObject _Reserved() => new() { ["aps"] = "x" };

    private static JsonObject _Filler(int length) => new() { ["k"] = new string('x', length) };

    #endregion

    #region Expiration and collapse id

    [Fact]
    public void should_send_expiration_zero_when_expiration_is_deliver_once()
    {
        // given
        var notification = new ApnsBackgroundNotification { Expiration = ApnsExpiration.DeliverOnce };

        // when
        var headers = _Prepare(notification).Headers;

        // then
        _Lines(headers).Should().Contain("apns-expiration: 0");
    }

    [Fact]
    public void should_send_expiration_epoch_seconds_when_expiration_is_absolute()
    {
        // given
        var notification = new ApnsAlertNotification
        {
            Alert = new ApnsAlert { Body = "Body" },
            Expiration = ApnsExpiration.At(_Now.AddHours(2)),
            CollapseId = "score",
        };

        // when
        var headers = _Prepare(notification).Headers;

        // then
        _Lines(headers)
            .Should()
            .Equal(
                "apns-push-type: alert",
                $"apns-topic: {_BundleId}",
                "apns-priority: 10",
                "apns-expiration: 1790337600",
                "apns-collapse-id: score"
            );
    }

    [Fact]
    public void should_throw_when_absolute_expiration_is_not_after_the_unix_epoch()
    {
        // when
        var act = () => ApnsExpiration.At(DateTimeOffset.UnixEpoch);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_omit_expiration_and_collapse_id_when_neither_is_set()
    {
        // when
        var headers = _Prepare(new ApnsBackgroundNotification()).Headers;

        // then
        headers.Enumerate().Select(h => h.Key).Should().NotContain(["apns-expiration", "apns-collapse-id"]);
    }

    [Fact]
    public void should_accept_a_collapse_id_of_exactly_64_bytes()
    {
        // given
        var notification = new ApnsBackgroundNotification { CollapseId = new string('c', 64) };

        // when
        var headers = _Prepare(notification).Headers;

        // then
        headers.CollapseId.Should().HaveLength(64);
    }

    [Fact]
    public void should_throw_when_collapse_id_exceeds_64_bytes()
    {
        // given: 63 ASCII bytes plus one two-byte character, so the length in chars stays under the limit.
        var notification = new ApnsBackgroundNotification { CollapseId = new string('c', 63) + "é" };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Data and size limits

    [Fact]
    public void should_throw_when_alert_data_uses_the_reserved_aps_key()
    {
        // given
        var notification = new ApnsAlertNotification
        {
            Alert = new ApnsAlert { Body = "Body" },
            Data = new JsonObject { ["aps"] = "x" },
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_background_data_uses_the_reserved_aps_key()
    {
        // given
        var notification = new ApnsBackgroundNotification { Data = new JsonObject { ["aps"] = "x" } };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_accept_a_payload_of_exactly_4096_bytes_and_refuse_one_more()
    {
        // given: {"aps":{"content-available":1},"k":"<filler>"} is 38 bytes of fixed JSON plus the filler.
        var atLimit = _BackgroundWithFiller(4096 - 38);
        var overLimit = _BackgroundWithFiller(4096 - 38 + 1);

        // when
        var accepted = _Prepare(atLimit);
        var act = () => _Prepare(overLimit);

        // then
        accepted.Payload.Should().HaveCount(4096);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_allow_5120_bytes_when_alert_is_sent_through_a_voip_instance()
    {
        // given: {"aps":{"alert":{"body":"Call"}},"k":"<filler>"} is 40 bytes of fixed JSON plus the filler.
        var atLimit = _AlertWithFiller(5120 - 40);
        var overLimit = _AlertWithFiller(5120 - 40 + 1);

        // when
        var accepted = _Prepare(atLimit, o => o.PushType = ApnsPushType.Voip);
        var act = () => _Prepare(overLimit, o => o.PushType = ApnsPushType.Voip);
        var actAsAlert = () => _Prepare(atLimit);

        // then
        accepted.Payload.Should().HaveCount(5120);
        act.Should().Throw<ArgumentException>();
        actAsAlert.Should().Throw<ArgumentException>();
    }

    #endregion

    #region JSON data

    [Fact]
    public void should_write_json_data_values_as_peers_of_aps()
    {
        // given
        var notification = new ApnsBackgroundNotification
        {
            Data = new JsonObject
            {
                ["order"] = new JsonObject { ["id"] = 42, ["tags"] = new JsonArray("a", "b") },
                ["urgent"] = true,
                ["ratio"] = 0.5,
                ["note"] = "hi",
                ["missing"] = null,
            },
        };

        // when
        var json = _Json(_Prepare(notification));

        // then
        json.Should()
            .Be(
                """{"aps":{"content-available":1},"order":{"id":42,"tags":["a","b"]},"urgent":true,"ratio":0.5,"note":"hi","missing":null}"""
            );
    }

    [Fact]
    public void should_leave_the_caller_data_untouched_when_preparing()
    {
        // given
        var nested = new JsonObject { ["id"] = 42 };
        var data = new JsonObject { ["order"] = nested, ["note"] = "hi" };
        var before = data.ToJsonString();
        var notification = new ApnsAlertNotification
        {
            Alert = new ApnsAlert { Body = "Body" },
            Data = data,
        };

        // when
        _Prepare(notification);
        _Prepare(notification);

        // then
        data.ToJsonString().Should().Be(before);
        data.Parent.Should().BeNull();
        nested.Parent.Should().BeSameAs(data);
        data.Count.Should().Be(2);
    }

    [Fact]
    public void should_count_json_data_toward_the_payload_limit()
    {
        // given: {"aps":{"content-available":1},"k":["<filler>"]} is 40 bytes of fixed JSON plus the filler.
        var atLimit = new ApnsBackgroundNotification
        {
            Data = new JsonObject { ["k"] = new JsonArray(new string('x', 4096 - 40)) },
        };
        var overLimit = new ApnsBackgroundNotification
        {
            Data = new JsonObject { ["k"] = new JsonArray(new string('x', 4096 - 40 + 1)) },
        };

        // when
        var accepted = _Prepare(atLimit);
        var act = () => _Prepare(overLimit);

        // then
        accepted.Payload.Should().HaveCount(4096);
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Raw notification

    public static TheoryData<ApnsNotificationType, string, string, string> RawTypes =>
        new()
        {
            { ApnsNotificationType.Alert, "alert", "", "10" },
            { ApnsNotificationType.Background, "background", "", "5" },
            { ApnsNotificationType.LiveActivity, "liveactivity", ".push-type.liveactivity", "5" },
            { ApnsNotificationType.Location, "location", ".location-query", "10" },
            { ApnsNotificationType.PushToTalk, "pushtotalk", ".voip-ptt", "10" },
            { ApnsNotificationType.Widgets, "widgets", ".push-type.widgets", "10" },
            { ApnsNotificationType.Controls, "controls", ".push-type.controls", "10" },
            { ApnsNotificationType.Complication, "complication", ".complication", "10" },
            { ApnsNotificationType.FileProvider, "fileprovider", ".pushkit.fileprovider", "10" },
        };

    [Theory]
    [MemberData(nameof(RawTypes))]
    public void should_send_a_raw_notification_with_the_headers_of_its_type(
        ApnsNotificationType type,
        string pushType,
        string topicSuffix,
        string priority
    )
    {
        // given
        var notification = new ApnsRawNotification { Type = type, Payload = _Element("""{"aps":{}}""") };

        // when
        var headers = _Prepare(notification).Headers;

        // then
        headers.PushType.Should().Be(pushType);
        headers.Topic.Should().Be(_BundleId + topicSuffix);
        _Lines(headers).Should().Contain($"apns-priority: {priority}");
    }

    [Fact]
    public void should_write_the_raw_payload_verbatim()
    {
        // given
        const string payload = """{ "aps" : { "alert" : "Hi", "future-key" : [1, 2] }, "custom": {"a": "éé"} }""";
        var notification = new ApnsRawNotification
        {
            Type = ApnsNotificationType.Alert,
            Payload = _Element(payload),
            Priority = ApnsPriority.PowerConsiderate,
            Expiration = ApnsExpiration.DeliverOnce,
            CollapseId = "c1",
        };

        // when
        var prepared = _Prepare(notification);

        // then
        _Json(prepared).Should().Be(payload);
        _Lines(prepared.Headers)
            .Should()
            .Equal(
                "apns-push-type: alert",
                $"apns-topic: {_BundleId}",
                "apns-priority: 5",
                "apns-expiration: 0",
                "apns-collapse-id: c1"
            );
    }

    [Fact]
    public void should_default_a_raw_push_to_talk_to_deliver_once()
    {
        // given
        var notification = new ApnsRawNotification
        {
            Type = ApnsNotificationType.PushToTalk,
            Payload = _Element("""{"aps":{}}"""),
        };

        // when
        var headers = _Prepare(notification).Headers;

        // then
        _Lines(headers).Should().Contain("apns-expiration: 0");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("1")]
    public void should_throw_when_the_raw_payload_is_not_a_json_object(string payload)
    {
        // given
        var notification = new ApnsRawNotification { Type = ApnsNotificationType.Alert, Payload = _Element(payload) };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("""{"aps":{},"k":1,}""")]
    [InlineData("""{"aps":{} /* note */,"k":1}""")]
    public void should_throw_when_the_raw_payload_was_parsed_leniently(string payload)
    {
        // given
        using var document = JsonDocument.Parse(
            payload,
            new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }
        );
        var notification = new ApnsRawNotification
        {
            Type = ApnsNotificationType.Alert,
            Payload = document.RootElement.Clone(),
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*strict JSON*");
    }

    [Fact]
    public void should_send_the_raw_payload_bytes_verbatim()
    {
        // given
        const string payload = """{ "aps" : { "alert" : "é" },  "k" : 1.0 }""";
        var notification = new ApnsRawNotification { Type = ApnsNotificationType.Alert, Payload = _Element(payload) };

        // when
        var prepared = _Prepare(notification);

        // then
        prepared.Payload.Should().Equal(Encoding.UTF8.GetBytes(payload));
    }

    [Fact]
    public void should_throw_when_the_raw_payload_is_undefined()
    {
        // given
        var notification = new ApnsRawNotification { Type = ApnsNotificationType.Alert, Payload = default };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_the_raw_type_is_undefined()
    {
        // given
        var notification = new ApnsRawNotification { Type = (ApnsNotificationType)99, Payload = _Element("{}") };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    public static TheoryData<ApnsNotificationType, ApnsPriority> RawDisallowedPriorities =>
        new()
        {
            { ApnsNotificationType.Background, ApnsPriority.Immediate },
            { ApnsNotificationType.Background, ApnsPriority.PowerPrioritized },
            { ApnsNotificationType.PushToTalk, ApnsPriority.PowerConsiderate },
            { ApnsNotificationType.LiveActivity, ApnsPriority.PowerPrioritized },
            { ApnsNotificationType.Location, ApnsPriority.PowerPrioritized },
            { ApnsNotificationType.Widgets, ApnsPriority.PowerPrioritized },
            { ApnsNotificationType.Controls, ApnsPriority.PowerPrioritized },
            { ApnsNotificationType.Complication, ApnsPriority.PowerPrioritized },
            { ApnsNotificationType.FileProvider, ApnsPriority.PowerPrioritized },
        };

    [Theory]
    [MemberData(nameof(RawDisallowedPriorities))]
    public void should_throw_when_a_raw_priority_breaks_its_type_rules(ApnsNotificationType type, ApnsPriority priority)
    {
        // given
        var notification = new ApnsRawNotification
        {
            Type = type,
            Payload = _Element("""{"aps":{}}"""),
            Priority = priority,
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_send_a_raw_alert_or_voip_as_voip_through_a_voip_instance()
    {
        // given
        var alert = new ApnsRawNotification { Type = ApnsNotificationType.Alert, Payload = _Element("""{"aps":{}}""") };
        var voip = alert with { Type = ApnsNotificationType.Voip };

        // when
        var alertHeaders = _Prepare(alert, o => o.PushType = ApnsPushType.Voip).Headers;
        var voipHeaders = _Prepare(voip, o => o.PushType = ApnsPushType.Voip).Headers;

        // then
        alertHeaders.PushType.Should().Be("voip");
        alertHeaders.Topic.Should().Be($"{_BundleId}.voip");
        voipHeaders.PushType.Should().Be("voip");
        voipHeaders.Topic.Should().Be($"{_BundleId}.voip");
    }

    [Fact]
    public void should_throw_when_a_raw_voip_is_sent_through_a_non_voip_instance()
    {
        // given
        var notification = new ApnsRawNotification { Type = ApnsNotificationType.Voip, Payload = _Element("{}") };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_a_voip_instance_gets_a_raw_non_voip_type()
    {
        // given
        var notification = new ApnsRawNotification
        {
            Type = ApnsNotificationType.Background,
            Payload = _Element("""{"aps":{"content-available":1}}"""),
        };

        // when
        var act = () => _Prepare(notification, o => o.PushType = ApnsPushType.Voip);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_apply_the_size_limit_of_the_raw_type()
    {
        // given: {"k":"<filler>"} is 8 bytes of fixed JSON plus the filler.
        var atAlertLimit = _RawWithFiller(ApnsNotificationType.Alert, 4096 - 8);
        var overAlertLimit = _RawWithFiller(ApnsNotificationType.Alert, 4096 - 8 + 1);
        var atVoipLimit = _RawWithFiller(ApnsNotificationType.Voip, 5120 - 8);
        var overVoipLimit = _RawWithFiller(ApnsNotificationType.Voip, 5120 - 8 + 1);

        // when
        var acceptedAlert = _Prepare(atAlertLimit);
        var actAlert = () => _Prepare(overAlertLimit);
        var acceptedVoip = _Prepare(atVoipLimit, o => o.PushType = ApnsPushType.Voip);
        var actVoip = () => _Prepare(overVoipLimit, o => o.PushType = ApnsPushType.Voip);

        // then
        acceptedAlert.Payload.Should().HaveCount(4096);
        actAlert.Should().Throw<ArgumentException>();
        acceptedVoip.Payload.Should().HaveCount(5120);
        actVoip.Should().Throw<ArgumentException>();
    }

    private static ApnsRawNotification _RawWithFiller(ApnsNotificationType type, int fillerLength)
    {
        return new ApnsRawNotification
        {
            Type = type,
            Payload = _Element($$"""{"k":"{{new string('x', fillerLength)}}"}"""),
        };
    }

    #endregion

    #region Live Activity push token

    [Fact]
    public void should_write_input_push_token_inside_aps_when_a_start_requests_a_push_token()
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = ApnsLiveActivityEvent.Start,
            ContentState = _Element("""{"score":0}"""),
            AttributesType = "MatchAttributes",
            Attributes = _Element("""{"home":"A"}"""),
            Alert = new ApnsAlert { Title = "Kickoff" },
            RequestPushToken = true,
        };

        // when
        var json = _Json(_Prepare(notification));

        // then
        json.Should()
            .Be(
                """{"aps":{"timestamp":1790330400,"event":"start","content-state":{"score":0},"input-push-token":1,"attributes-type":"MatchAttributes","attributes":{"home":"A"},"alert":{"title":"Kickoff"}}}"""
            );
    }

    [Theory]
    [InlineData(ApnsLiveActivityEvent.Update)]
    [InlineData(ApnsLiveActivityEvent.End)]
    public void should_throw_when_a_non_start_event_requests_a_push_token(ApnsLiveActivityEvent activityEvent)
    {
        // given
        var notification = new ApnsLiveActivityNotification
        {
            Event = activityEvent,
            ContentState = _Element("""{"score":1}"""),
            RequestPushToken = true,
        };

        // when
        var act = () => _Prepare(notification);

        // then
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Helpers

    private ApnsPreparedNotification _Prepare(ApnsNotification notification, Action<ApnsOptions>? configure = null)
    {
        var options = new ApnsOptions
        {
            KeyId = "ABC123DEFG",
            TeamId = "DEF123GHIJ",
            PrivateKey = "unused",
            BundleId = _BundleId,
        };

        configure?.Invoke(options);

        return ApnsPayloadWriter.Prepare(notification, options, _clock);
    }

    private static string[] _Lines(ApnsRequestHeaders headers) =>
        [.. headers.Enumerate().Select(h => $"{h.Key}: {h.Value}")];

    private static string _Json(ApnsPreparedNotification prepared) => Encoding.UTF8.GetString(prepared.Payload);

    private static JsonElement _Element(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    private static ApnsBackgroundNotification _BackgroundWithFiller(int fillerLength)
    {
        return new ApnsBackgroundNotification { Data = new JsonObject { ["k"] = new string('x', fillerLength) } };
    }

    private static ApnsAlertNotification _AlertWithFiller(int fillerLength)
    {
        return new ApnsAlertNotification
        {
            Alert = new ApnsAlert { Body = "Call" },
            Data = new JsonObject { ["k"] = new string('x', fillerLength) },
        };
    }

    #endregion
}
