// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Headless.PushNotifications.Apns;
using Headless.PushNotifications.Apns.Internals;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.CrossLibrary;

/// <summary>
/// Compares the request we build for each shared scenario against the request three reference APNs libraries build
/// for it (see <c>eng/apns-oracles/README.md</c>). Every difference must be listed in <c>divergences.json</c> with the
/// Apple page or library source that justifies it, and a listed difference that no longer occurs fails too, so the
/// list cannot go stale.
/// </summary>
public sealed class ApnsCrossLibraryConformanceTests : TestBase
{
    private const string _BundleId = "com.example.app";
    private const string _KeyId = "TESTKEY123";
    private const string _TeamId = "TESTTEAM12";
    private const string _JwtScenario = "jwt";
    private const string _AnyScenario = "*";

    // Our send path stamps a fresh apns-id on every request without a caller id, where a library that sends none
    // leaves APNs to assign it. The random value is replaced by this marker so the difference can be listed.
    private const string _GeneratedApnsId = "<generated>";

    // A key one side does not send, kept distinct from a JSON null.
    private const string _Absent = "<absent>";

    // Fixture marker: the library stamps iat from the wall clock, so only its type is recorded.
    private const string _AnyNumber = "<number>";

    private static readonly string[] _Libraries = ["node-apn", "pushy", "apns2"];

    private static readonly string[] _ComparedHeaders =
    [
        "apns-push-type",
        "apns-topic",
        "apns-priority",
        "apns-expiration",
        "apns-collapse-id",
        "apns-id",
    ];

    private static readonly string _Directory = Path.Combine(AppContext.BaseDirectory, "CrossLibrary");

    private static readonly Lazy<JsonObject> _Scenarios = new(() => _Load("scenarios.json"));

    private static readonly Lazy<Dictionary<string, JsonObject>> _Fixtures = new(() =>
        _Libraries.ToDictionary(
            library => library,
            library => _Load(Path.Combine("Fixtures", $"{library}.json")),
            StringComparer.Ordinal
        )
    );

    private static readonly Lazy<List<Divergence>> _Divergences = new(() =>
        [
            .. _Load("divergences.json")["divergences"]!
                .AsArray()
                .Select(entry => new Divergence(
                    _String(entry, "library"),
                    _String(entry, "scenario"),
                    _String(entry, "field"),
                    entry!["ours"]?.DeepClone(),
                    entry["theirs"]?.DeepClone(),
                    _String(entry, "reason"),
                    _String(entry, "evidence")
                )),
        ]
    );

    public static TheoryData<string, string> Rows
    {
        get
        {
            var rows = new TheoryData<string, string>();

            foreach (var library in _Libraries)
            {
                foreach (var scenario in _ScenarioIds())
                {
                    rows.Add(library, scenario);
                }
            }

            return rows;
        }
    }

    public static TheoryData<string> Libraries => [.. _Libraries];

    #region Requests

    [Theory]
    [MemberData(nameof(Rows))]
    public void should_send_the_request_the_reference_library_sends_or_a_listed_divergence(
        string library,
        string scenario
    )
    {
        // given
        var row = _FixtureRow(library, scenario);

        if (!row["supported"]!.GetValue<bool>())
        {
            Assert.Skip($"{library} cannot express '{scenario}': {row["unsupportedReason"]?.GetValue<string>()}");
        }

        // when
        var differences = _RequestDifferences(scenario, row);

        // then
        _AssertOnlyListedDifferences(library, scenario, differences);
    }

    [Fact]
    public void should_record_fixtures_generated_by_the_pinned_library_versions()
    {
        // A pin bump without `make apns-oracles` would leave fixtures describing the old library.
        var pins = Path.Combine(AppContext.BaseDirectory, "CrossLibrary", "Pins");
        var nodeApn = JsonNode.Parse(File.ReadAllText(Path.Combine(pins, "package.json")))!["dependencies"]![
            "@parse/node-apn"
        ]!.GetValue<string>();
        var pushy = XDocument
            .Load(Path.Combine(pins, "pom.xml"))
            .Descendants()
            .First(e => e.Name.LocalName == "artifactId" && e.Value == "pushy")
            .ElementsAfterSelf()
            .First(e => e.Name.LocalName == "version")
            .Value;
        var apns2 = File.ReadAllLines(Path.Combine(pins, "go.mod"))
            .Select(line => line.Trim())
            .First(line => line.StartsWith("require github.com/sideshow/apns2 ", StringComparison.Ordinal))
            .Split(' ')[^1];

        foreach (var (library, pinned) in new[] { ("node-apn", nodeApn), ("pushy", pushy), ("apns2", apns2) })
        {
            _String(_Fixtures.Value[library], "version")
                .Should()
                .Be(pinned, $"the {library} fixture must be regenerated with `make apns-oracles` after a pin change");
        }
    }

    [Fact]
    public void should_record_every_scenario_in_every_fixture()
    {
        foreach (var library in _Libraries)
        {
            var recorded = _Fixtures.Value[library]["scenarios"]!.AsArray().Select(row => _String(row, "id"));

            recorded.Should().BeEquivalentTo(_ScenarioIds(), $"the {library} fixture records every shared scenario");
        }
    }

    [Fact]
    public void should_still_observe_every_divergence_listed_for_any_scenario()
    {
        // A wildcard entry covers every supported row of its library, so it is stale only when no row produces it.
        var stale = new List<string>();

        foreach (
            var divergence in _Divergences.Value.Where(d =>
                string.Equals(d.Scenario, _AnyScenario, StringComparison.Ordinal)
            )
        )
        {
            var observed = _SupportedRows(divergence.Library)
                .Any(row => _RequestDifferences(_String(row, "id"), row).Exists(divergence.Matches));

            if (!observed)
            {
                stale.Add($"  {divergence.Describe()}");
            }
        }

        stale
            .Should()
            .BeEmpty("a wildcard divergence that no row produces anymore must be removed from divergences.json");
    }

    [Fact]
    public void should_list_each_divergence_against_a_row_that_is_compared()
    {
        var scenarios = _ScenarioIds().ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var divergence in _Divergences.Value)
        {
            if (!_Libraries.Contains(divergence.Library, StringComparer.Ordinal))
            {
                problems.Add($"  unknown library: {divergence.Describe()}");

                continue;
            }

            if (string.IsNullOrWhiteSpace(divergence.Reason) || string.IsNullOrWhiteSpace(divergence.Evidence))
            {
                problems.Add($"  missing reason or evidence: {divergence.Describe()}");
            }

            if (divergence.Scenario is _AnyScenario or _JwtScenario)
            {
                continue;
            }

            if (!scenarios.Contains(divergence.Scenario))
            {
                problems.Add($"  unknown scenario: {divergence.Describe()}");
            }
            else if (!_FixtureRow(divergence.Library, divergence.Scenario)["supported"]!.GetValue<bool>())
            {
                // The row is skipped, so the entry would never be checked.
                problems.Add($"  row is unsupported and never compared: {divergence.Describe()}");
            }
        }

        problems.Should().BeEmpty("every divergences.json entry must name a compared row and justify itself");
    }

    private static List<Difference> _RequestDifferences(string scenario, JsonObject row)
    {
        var ours = _OurRequest(scenario);
        var differences = new List<Difference>();

        _Diff("headers", ours["headers"], row["headers"], differences);
        _Diff("payload", ours["payload"], row["payload"], differences);

        return differences;
    }

    private static JsonObject _OurRequest(string scenario)
    {
        var (notification, voipInstance) = _OurNotification(scenario);

        var options = new ApnsOptions
        {
            BundleId = _BundleId,
            PushType = voipInstance ? ApnsPushType.Voip : ApnsPushType.Alert,
        };

        var prepared = ApnsPayloadWriter.Prepare(notification, options, new FakeTimeProvider(_IssuedAt()));

        var headers = new JsonObject();

        foreach (var name in _ComparedHeaders)
        {
            headers[name] = null;
        }

        foreach (var (name, value) in prepared.Headers.Enumerate())
        {
            headers[name] = value;
        }

        // Prepare does not decide the apns-id; the send path does, as the caller's id when one is set and a fresh
        // Guid otherwise (ApnsPushNotificationService._SendOneAsync).
        headers["apns-id"] = notification.ApnsId is { } apnsId ? apnsId.ToString("D") : _GeneratedApnsId;

        return new JsonObject { ["headers"] = headers, ["payload"] = JsonNode.Parse(prepared.Payload) };
    }

    #endregion

    #region Scenario mapping

    // Each scenario in eng/apns-oracles/scenarios.json, expressed through our public typed API. The mapping is written
    // out rather than derived from the scenario JSON so that it exercises the API a caller would use.
    private static (ApnsNotification Notification, bool VoipInstance) _OurNotification(string scenario)
    {
        return scenario switch
        {
            "alert-basic" => (_Alert("Hello", "World"), false),
            "alert-full" => (_FullAlert(targetContentId: "order-42"), false),
            "alert-full-without-target-content-id" => (_FullAlert(targetContentId: null), false),
            "alert-critical-sound" => (
                new ApnsAlertNotification
                {
                    Alert = new ApnsAlert { Title = "Critical", Body = "Check the sensor" },
                    InterruptionLevel = ApnsInterruptionLevel.Critical,
                    Sound = ApnsSound.Critical("chime.caf", volume: 0.8),
                },
                false
            ),
            // Background pushes always go at priority 5, the scenario's priority, so the type takes no priority.
            "background" => (new ApnsBackgroundNotification { Data = new JsonObject { ["sync"] = "inbox" } }, false),
            // The typed API has no data-only VoIP type: a data-only request on a VoIP instance becomes this internal
            // notification, which is what the shared PushNotificationRequest path sends.
            "voip" => (
                new ApnsVoipDataNotification
                {
                    Data = new JsonObject { ["caller"] = "Alice", ["callId"] = "call-7" },
                },
                true
            ),
            "liveactivity-start" => (
                new ApnsLiveActivityNotification
                {
                    Event = ApnsLiveActivityEvent.Start,
                    Timestamp = DateTimeOffset.FromUnixTimeSeconds(1767225600),
                    AttributesType = "DeliveryAttributes",
                    Attributes = _Element("""{"orderId":"42","restaurant":"Pizza Place"}"""),
                    ContentState = _Element("""{"status":"preparing","etaMinutes":25}"""),
                    Alert = new ApnsAlert { Title = "Order started", Body = "We are preparing your order" },
                    Sound = ApnsSound.Named("chime.caf"),
                },
                false
            ),
            "liveactivity-update" => (
                new ApnsLiveActivityNotification
                {
                    Event = ApnsLiveActivityEvent.Update,
                    Timestamp = DateTimeOffset.FromUnixTimeSeconds(1767225600),
                    ContentState = _Element("""{"status":"on-the-way","etaMinutes":10}"""),
                    StaleDate = DateTimeOffset.FromUnixTimeSeconds(1767229200),
                    RelevanceScore = 0.5,
                },
                false
            ),
            "liveactivity-end" => (
                new ApnsLiveActivityNotification
                {
                    Event = ApnsLiveActivityEvent.End,
                    Timestamp = DateTimeOffset.FromUnixTimeSeconds(1767225600),
                    ContentState = _Element("""{"status":"delivered","etaMinutes":0}"""),
                    DismissalDate = DateTimeOffset.FromUnixTimeSeconds(1767232800),
                },
                false
            ),
            "location" => (new ApnsLocationNotification(), false),
            "pushtotalk" => (
                new ApnsPushToTalkNotification { Data = new JsonObject { ["activeSpeaker"] = "Alice" } },
                false
            ),
            "widgets" => (new ApnsWidgetsNotification(), false),
            "controls" => (new ApnsControlsNotification(), false),
            "complication" => (
                new ApnsComplicationNotification { Data = new JsonObject { ["temperature"] = 21 } },
                false
            ),
            "fileprovider" => (
                new ApnsFileProviderNotification
                {
                    ContainerIdentifier = "NSFileProviderRootContainerItemIdentifier",
                    Domain = "com.example.app.fileprovider.domain",
                },
                false
            ),
            "custom-data-nested" => (
                new ApnsAlertNotification
                {
                    Alert = new ApnsAlert { Body = "You have a new order" },
                    Data = JsonNode
                        .Parse(
                            """
                            {
                              "order": { "id": "A-1", "lines": [{ "sku": "X1", "qty": 2 }], "total": 12.5, "paid": true },
                              "tags": ["new", "priority"],
                              "count": 3,
                              "flag": false
                            }
                            """
                        )!
                        .AsObject(),
                },
                false
            ),
            "expiration-zero" => (
                _Alert("Now", "Deliver now or never") with
                {
                    Expiration = ApnsExpiration.DeliverOnce,
                },
                false
            ),
            "expiration-absolute" => (
                _Alert("Later", "Keep for a day") with
                {
                    Expiration = ApnsExpiration.At(DateTimeOffset.FromUnixTimeSeconds(1767312000)),
                },
                false
            ),
            "collapse-id" => (_Alert("Order 42", "Latest status") with { CollapseId = "order-42" }, false),
            "priority-5" => (
                _Alert("Low", "Power-considerate delivery") with
                {
                    Priority = ApnsPriority.PowerConsiderate,
                },
                false
            ),
            "priority-10" => (_Alert("High", "Immediate delivery") with { Priority = ApnsPriority.Immediate }, false),
            "apns-id" => (
                _Alert("Tracked", "Caller-chosen id") with
                {
                    ApnsId = Guid.Parse("123e4567-e89b-12d3-a456-426614174000"),
                },
                false
            ),
            "raw-payload" => (
                new ApnsRawNotification
                {
                    Type = ApnsNotificationType.Alert,
                    Payload = _Element(
                        """
                        {
                          "aps": {
                            "alert": { "title": "Raw", "body": "Unmodelled keys pass through" },
                            "sound": "default",
                            "future-apple-key": { "enabled": true }
                          },
                          "customRoot": [1, "two", { "three": 3 }]
                        }
                        """
                    ),
                },
                false
            ),
            _ => throw new InvalidOperationException(
                $"Scenario '{scenario}' has no mapping to our API; add one to {nameof(_OurNotification)}."
            ),
        };
    }

    private static ApnsAlertNotification _Alert(string title, string body)
    {
        return new ApnsAlertNotification
        {
            Alert = new ApnsAlert { Title = title, Body = body },
        };
    }

    private static ApnsAlertNotification _FullAlert(string? targetContentId)
    {
        // The scenario sets both the literal title and body and their localization keys. Our API refuses a literal
        // with its localization key, because Apple reads the key "instead of" the literal, so only the keys are set;
        // divergences.json lists the literals the libraries send beside them.
        return new ApnsAlertNotification
        {
            Alert = new ApnsAlert
            {
                Subtitle = "Arrives tomorrow",
                TitleLocKey = "ORDER_TITLE",
                TitleLocArgs = ["42"],
                LocKey = "ORDER_BODY",
                LocArgs = ["42", "tomorrow"],
                LaunchImage = "launch.png",
            },
            ThreadId = "orders",
            Category = "ORDER_UPDATE",
            MutableContent = true,
            TargetContentId = targetContentId,
            InterruptionLevel = ApnsInterruptionLevel.TimeSensitive,
            RelevanceScore = 0.5,
            Badge = 0,
            Sound = ApnsSound.Named("chime.caf"),
        };
    }

    #endregion

    #region Provider token

    [Theory]
    [MemberData(nameof(Libraries))]
    public async Task should_mint_the_provider_token_shape_the_reference_library_mints_or_a_listed_divergence(
        string library
    )
    {
        // given
        var recorded = _Fixtures.Value[library]["jwt"]!;

        // when
        var (header, claims, _, _) = await _OurToken();

        // then
        var differences = new List<Difference>();
        _Diff("header", header, recorded["header"], differences);
        _Diff("claims", claims, recorded["claims"], differences);

        _AssertOnlyListedDifferences(library, _JwtScenario, differences);
    }

    [Fact]
    public async Task should_sign_the_provider_token_with_es256_verifiable_by_the_public_key()
    {
        // given
        var (_, _, signingInput, signature) = await _OurToken();

        using var privateKey = ECDsa.Create();
        privateKey.ImportFromPem(await File.ReadAllTextAsync(_KeyPath(), AbortToken));

        using var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(privateKey.ExportSubjectPublicKeyInfo(), out _);

        // then
        signature.Should().HaveCount(64, "JWS ES256 is the fixed-width IEEE P1363 r||s form");
        publicKey
            .VerifyData(
                Encoding.ASCII.GetBytes(signingInput),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation
            )
            .Should()
            .BeTrue();
    }

    private async Task<(JsonNode Header, JsonNode Claims, string SigningInput, byte[] Signature)> _OurToken()
    {
        using var source = new ApnsTokenSource(new FakeTimeProvider(_IssuedAt()));

        var options = new ApnsOptions
        {
            BundleId = _BundleId,
            KeyId = _KeyId,
            TeamId = _TeamId,
            PrivateKey = await File.ReadAllTextAsync(_KeyPath(), AbortToken),
        };

        var token = await source.GetTokenAsync(options, AbortToken);
        var segments = token.Value.Split('.');

        return (
            JsonNode.Parse(Base64Url.DecodeFromChars(segments[0]))!,
            JsonNode.Parse(Base64Url.DecodeFromChars(segments[1]))!,
            $"{segments[0]}.{segments[1]}",
            Base64Url.DecodeFromChars(segments[2])
        );
    }

    // pushy is the one library whose token takes a fixed issue time, so its recorded iat pins our clock.
    private static DateTimeOffset _IssuedAt()
    {
        return DateTimeOffset.FromUnixTimeSeconds(_Fixtures.Value["pushy"]["jwt"]!["claims"]!["iat"]!.GetValue<long>());
    }

    private static string _KeyPath() => Path.Combine(_Directory, "AuthKey_TESTKEY123.TEST-ONLY.p8");

    #endregion

    #region Comparison

    private static void _Diff(string path, JsonNode? ours, JsonNode? theirs, List<Difference> differences)
    {
        if (ours is JsonObject ourObject && theirs is JsonObject theirObject)
        {
            var keys = ourObject
                .Select(p => p.Key)
                .Union(theirObject.Select(p => p.Key), StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);

            foreach (var key in keys)
            {
                _Diff(
                    $"{path}.{key}",
                    ourObject.TryGetPropertyValue(key, out var ourValue) ? ourValue : JsonValue.Create(_Absent),
                    theirObject.TryGetPropertyValue(key, out var theirValue) ? theirValue : JsonValue.Create(_Absent),
                    differences
                );
            }

            return;
        }

        if (ours is JsonArray ourArray && theirs is JsonArray theirArray && ourArray.Count == theirArray.Count)
        {
            for (var i = 0; i < ourArray.Count; i++)
            {
                _Diff($"{path}[{i.ToString(CultureInfo.InvariantCulture)}]", ourArray[i], theirArray[i], differences);
            }

            return;
        }

        if (!_SameValue(path, ours, theirs))
        {
            differences.Add(new Difference(path, ours?.DeepClone(), theirs?.DeepClone()));
        }
    }

    private static bool _SameValue(string path, JsonNode? ours, JsonNode? theirs)
    {
        if (ours is null || theirs is null)
        {
            return ours is null && theirs is null;
        }

        var ourKind = ours.GetValueKind();
        var theirKind = theirs.GetValueKind();

        if (
            path.StartsWith("claims.", StringComparison.Ordinal)
            && theirKind == JsonValueKind.String
            && string.Equals(theirs.GetValue<string>(), _AnyNumber, StringComparison.Ordinal)
        )
        {
            return ourKind == JsonValueKind.Number
                && long.TryParse(ours.ToJsonString(), CultureInfo.InvariantCulture, out _);
        }

        // By value, so 1, 1.0, and 1e0 compare equal whatever the library's serializer writes.
        if (ourKind == JsonValueKind.Number && theirKind == JsonValueKind.Number)
        {
            return decimal.Parse(ours.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture)
                == decimal.Parse(theirs.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        return JsonNode.DeepEquals(ours, theirs);
    }

    private static void _AssertOnlyListedDifferences(string library, string scenario, List<Difference> differences)
    {
        var listed = _Divergences
            .Value.Where(d =>
                string.Equals(d.Library, library, StringComparison.Ordinal)
                && (
                    string.Equals(d.Scenario, scenario, StringComparison.Ordinal)
                    || (
                        string.Equals(d.Scenario, _AnyScenario, StringComparison.Ordinal)
                        && !string.Equals(scenario, _JwtScenario, StringComparison.Ordinal)
                    )
                )
            )
            .ToList();

        var unlisted = differences.Where(difference => !listed.Exists(d => d.Matches(difference))).ToList();

        // Wildcard entries are checked for staleness across all rows in their own test.
        var stale = listed
            .Where(d =>
                !string.Equals(d.Scenario, _AnyScenario, StringComparison.Ordinal) && !differences.Exists(d.Matches)
            )
            .Select(d => d.Describe())
            .ToList();

        if (unlisted.Count == 0 && stale.Count == 0)
        {
            return;
        }

        var report = new StringBuilder();
        report.AppendLine(
            CultureInfo.InvariantCulture,
            $"{library} / {scenario}: our request differs from the reference."
        );

        if (unlisted.Count > 0)
        {
            report.AppendLine("Unlisted differences (fix our code, or justify each one in divergences.json):");

            foreach (var difference in unlisted)
            {
                report.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"  {difference.Field}: ours={_Show(difference.Ours)} theirs={_Show(difference.Theirs)}"
                );
                report.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"    {{\"library\":\"{library}\",\"scenario\":\"{scenario}\",\"field\":\"{difference.Field}\",\"ours\":{_Show(difference.Ours)},\"theirs\":{_Show(difference.Theirs)}}}"
                );
            }
        }

        if (stale.Count > 0)
        {
            report.AppendLine("Listed divergences that no longer occur (remove them from divergences.json):");

            foreach (var entry in stale)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  {entry}");
            }
        }

#pragma warning disable FAA0002 // Fix would make it worse: AwesomeAssertions reads its failure message as a template, and this report carries JSON braces.
        Assert.Fail(report.ToString());
#pragma warning restore FAA0002
    }

    private static string _Show(JsonNode? node) => node?.ToJsonString() ?? "null";

    #endregion

    #region Loading

    private static IEnumerable<string> _ScenarioIds()
    {
        return _Scenarios.Value["scenarios"]!.AsArray().Select(scenario => _String(scenario, "id"));
    }

    private static JsonObject _FixtureRow(string library, string scenario)
    {
        return _Fixtures.Value[library]["scenarios"]!
            .AsArray()
            .Single(row => string.Equals(_String(row, "id"), scenario, StringComparison.Ordinal))!
            .AsObject();
    }

    private static IEnumerable<JsonObject> _SupportedRows(string library)
    {
        return _Fixtures.Value[library]["scenarios"]!
            .AsArray()
            .Select(row => row!.AsObject())
            .Where(row => row["supported"]!.GetValue<bool>());
    }

    private static JsonObject _Load(string relativePath)
    {
#pragma warning disable MA0045 // False positive: it also feeds MemberData, which xUnit discovers synchronously.
        return JsonNode.Parse(File.ReadAllText(Path.Combine(_Directory, relativePath)))!.AsObject();
#pragma warning restore MA0045
    }

    private static string _String(JsonNode? node, string property) => node![property]!.GetValue<string>();

    private static JsonElement _Element(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    #endregion

    private sealed record Difference(string Field, JsonNode? Ours, JsonNode? Theirs);

    private sealed record Divergence(
        string Library,
        string Scenario,
        string Field,
        JsonNode? Ours,
        JsonNode? Theirs,
        string Reason,
        string Evidence
    )
    {
        public bool Matches(Difference difference)
        {
            return string.Equals(Field, difference.Field, StringComparison.Ordinal)
                && JsonNode.DeepEquals(Ours, difference.Ours)
                && JsonNode.DeepEquals(Theirs, difference.Theirs);
        }

        public string Describe()
        {
            return $"{Library} / {Scenario} / {Field}: ours={_Show(Ours)} theirs={_Show(Theirs)}";
        }
    }
}
