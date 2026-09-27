// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications.Apns;
using Headless.PushNotifications.Apns.Internals;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// Guards the two switches every push type must appear in, <see cref="ApnsPayloadWriter.Prepare"/> and
/// <see cref="ApnsRequestHeaders.Create"/>: a push type added to only one of them fails here instead of at send
/// time.
/// </summary>
public sealed class ApnsPushTypeCompletenessTests : TestBase
{
    private const string _BundleId = "com.example.app";

    /// <summary>
    /// One minimal valid instance per concrete push type, whether the instance must be configured for VoIP to send
    /// it, and the <c>apns-push-type</c> header it must produce. A new push type needs an entry here.
    /// </summary>
    private static readonly Dictionary<Type, (Func<ApnsNotification> Create, bool Voip, string PushType)> _Factories =
        new()
        {
            [typeof(ApnsAlertNotification)] = (
                static () => new ApnsAlertNotification { Alert = new ApnsAlert { Body = "Hi" } },
                false,
                ApnsPushTypes.Alert
            ),
            [typeof(ApnsBackgroundNotification)] = (
                static () =>
                    new ApnsBackgroundNotification
                    {
                        Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["sync"] = "1" },
                    },
                false,
                ApnsPushTypes.Background
            ),
            [typeof(ApnsLiveActivityNotification)] = (
                static () => new ApnsLiveActivityNotification { Event = ApnsLiveActivityEvent.End },
                false,
                ApnsPushTypes.LiveActivity
            ),
            [typeof(ApnsLocationNotification)] = (
                static () => new ApnsLocationNotification(),
                false,
                ApnsPushTypes.Location
            ),
            [typeof(ApnsPushToTalkNotification)] = (
                static () => new ApnsPushToTalkNotification(),
                false,
                ApnsPushTypes.PushToTalk
            ),
            [typeof(ApnsWidgetsNotification)] = (
                static () => new ApnsWidgetsNotification(),
                false,
                ApnsPushTypes.Widgets
            ),
            [typeof(ApnsControlsNotification)] = (
                static () => new ApnsControlsNotification(),
                false,
                ApnsPushTypes.Controls
            ),
            [typeof(ApnsComplicationNotification)] = (
                static () => new ApnsComplicationNotification(),
                false,
                ApnsPushTypes.Complication
            ),
            [typeof(ApnsFileProviderNotification)] = (
                static () => new ApnsFileProviderNotification { ContainerIdentifier = "c", Domain = "d" },
                false,
                ApnsPushTypes.FileProvider
            ),
            [typeof(ApnsVoipDataNotification)] = (
                static () =>
                    new ApnsVoipDataNotification
                    {
                        Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["call"] = "1" },
                    },
                true,
                ApnsPushTypes.Voip
            ),
        };

    [Fact]
    public void should_prepare_every_concrete_push_type_with_its_push_type_header()
    {
        // given
        var pushTypes = typeof(ApnsNotification)
            .Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsSealed: true })
            .Where(t => t.IsSubclassOf(typeof(ApnsNotification)))
            .ToList();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));

        // then
        pushTypes.Should().NotBeEmpty();
        var missing = pushTypes.Where(t => !_Factories.ContainsKey(t)).Select(t => t.Name).ToList();
        missing
            .Should()
            .BeEmpty(
                "every concrete ApnsNotification needs a factory entry in {0}, so the test proves it goes through both ApnsPayloadWriter.Prepare and ApnsRequestHeaders.Create",
                nameof(_Factories)
            );

        foreach (var type in pushTypes)
        {
            var (create, voip, expectedPushType) = _Factories[type];
            var notification = create();
            var options = new ApnsOptions
            {
                BundleId = _BundleId,
                PushType = voip ? ApnsPushType.Voip : ApnsPushType.Alert,
            };

            // when
            var act = () => ApnsPayloadWriter.Prepare(notification, options, clock);

            // then
            var prepared = act.Should().NotThrow($"{type.Name} must be handled by both switches").Subject;
            prepared.Headers.PushType.Should().Be(expectedPushType, type.Name);
            prepared.Payload.Should().NotBeEmpty(type.Name);
        }
    }
}
