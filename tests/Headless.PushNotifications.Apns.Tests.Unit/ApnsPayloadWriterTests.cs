// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
            Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = "v" },
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
        _Lines(headers).Should().Equal("apns-push-type: voip", $"apns-topic: {_BundleId}.voip", "apns-priority: 10");
    }

    #endregion

    #region Background

    [Fact]
    public void should_write_content_available_with_top_level_data_when_notification_is_background()
    {
        // given
        var notification = new ApnsBackgroundNotification
        {
            Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["sync"] = "1" },
        };

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

    private static Dictionary<string, string> _KeyValue() => new(StringComparer.Ordinal) { ["k"] = "v" };

    private static Dictionary<string, string> _Reserved() => new(StringComparer.Ordinal) { ["aps"] = "x" };

    private static Dictionary<string, string> _Filler(int length) =>
        new(StringComparer.Ordinal) { ["k"] = new string('x', length) };

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
            Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["aps"] = "x" },
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
        var notification = new ApnsBackgroundNotification
        {
            Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["aps"] = "x" },
        };

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
        return new ApnsBackgroundNotification
        {
            Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = new string('x', fillerLength) },
        };
    }

    private static ApnsAlertNotification _AlertWithFiller(int fillerLength)
    {
        return new ApnsAlertNotification
        {
            Alert = new ApnsAlert { Body = "Call" },
            Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = new string('x', fillerLength) },
        };
    }

    #endregion
}
