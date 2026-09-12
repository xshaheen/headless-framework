// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

/// <summary>One field (or subject) on which the desired stream configuration and the live stream disagree.</summary>
/// <param name="Field">The <c>StreamConfig</c> property name, or <c>Subjects</c> for uncovered subjects.</param>
/// <param name="Desired">The value this application asks for, rendered for display.</param>
/// <param name="Actual">The value the live stream carries, rendered for display.</param>
/// <param name="IsImmutable">
/// <see langword="true"/> when JetStream refuses to change this field on an existing stream, so no
/// provisioning mode can converge it and the stream must be recreated or migrated out of band.
/// </param>
internal sealed record StreamDivergence(string Field, string? Desired, string? Actual, bool IsImmutable);

/// <summary>
/// Compares a desired <c>StreamConfig</c> against the live stream and renders the difference as an operator-facing
/// diagnostic. Split out of <c>NatsConsumerClient</c> so the comparison and the message can be tested without a broker.
/// </summary>
internal static class NatsStreamReconciliation
{
    /// <summary>
    /// Stream identity. The provider owns these and a separate guard already rejects a <c>StreamOptions</c>
    /// callback that alters them. Retention must still enter the comparison below because the identity guard
    /// runs after the provider asserts the lane-required value. Subjects are compared separately and by
    /// coverage rather than equality — see <see cref="FindUncoveredSubjects"/>.
    /// </summary>
    private static readonly HashSet<string> _IdentityFields = new(StringComparer.Ordinal)
    {
        nameof(StreamConfig.Name),
        nameof(StreamConfig.Subjects),
    };

    /// <summary>
    /// Fields NATS server 2.9 accepts when updating a stream. The update validator in NATS server
    /// <c>server/stream.go</c> does not special-case these fields, while MaxConsumers, Storage, Retention,
    /// Mirror, RePublish, and one-way deletion/purge/seal flags are restricted. Anything outside this explicit
    /// list is over-reported as immutable so an unverified field cannot reach an update that the server rejects.
    /// </summary>
    private static readonly HashSet<string> _ReconcilableFields = new(StringComparer.Ordinal)
    {
        nameof(StreamConfig.Description),
        nameof(StreamConfig.Discard),
        nameof(StreamConfig.DuplicateWindow),
        nameof(StreamConfig.MaxAge),
        nameof(StreamConfig.MaxBytes),
        nameof(StreamConfig.MaxMsgSize),
        nameof(StreamConfig.MaxMsgs),
        nameof(StreamConfig.MaxMsgsPerSubject),
        nameof(StreamConfig.Metadata),
        nameof(StreamConfig.NoAck),
        nameof(StreamConfig.NumReplicas),
    };

    private static readonly PropertyInfo[] _ComparableProperties =
    [
        .. typeof(StreamConfig)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && !_IdentityFields.Contains(p.Name))
            .OrderBy(p => p.Name, StringComparer.Ordinal),
    ];

    /// <summary>
    /// Captures the comparable property values of <paramref name="config"/>. Taking one snapshot before the
    /// <c>StreamOptions</c> callback runs and one after identifies exactly which fields the callback asserted,
    /// which is what keeps server-defaulted fields the provider never set out of the comparison.
    /// </summary>
    public static Dictionary<string, object?> Snapshot(StreamConfig config)
    {
        var values = new Dictionary<string, object?>(_ComparableProperties.Length, StringComparer.Ordinal);

        foreach (var property in _ComparableProperties)
        {
            values[property.Name] = property.GetValue(config);
        }

        return values;
    }

    /// <summary>
    /// Returns the property names whose value the <c>StreamOptions</c> callback changed, given the snapshots
    /// taken either side of it.
    /// </summary>
    public static HashSet<string> AssertedFields(
        IReadOnlyDictionary<string, object?> before,
        IReadOnlyDictionary<string, object?> after
    )
    {
        var asserted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, afterValue) in after)
        {
            if (!before.TryGetValue(name, out var beforeValue) || !Equals(beforeValue, afterValue))
            {
                asserted.Add(name);
            }
        }

        return asserted;
    }

    /// <summary>
    /// Compares the fields this application actually asserts against the live stream. A field neither the
    /// provider nor the operator's callback set is never compared: the server fills those with its own defaults,
    /// and diffing them would report drift on every stream.
    /// </summary>
    public static IReadOnlyList<StreamDivergence> CompareFields(
        StreamConfig desired,
        StreamConfig live,
        IReadOnlySet<string> assertedFields
    )
    {
        var divergences = new List<StreamDivergence>();

        foreach (var property in _ComparableProperties)
        {
            if (!assertedFields.Contains(property.Name))
            {
                continue;
            }

            var desiredValue = property.GetValue(desired);
            var liveValue = property.GetValue(live);

            if (Equals(desiredValue, liveValue))
            {
                continue;
            }

            if (_StructurallyEqual(desiredValue, liveValue))
            {
                continue;
            }

            divergences.Add(
                new StreamDivergence(
                    property.Name,
                    _Render(desiredValue),
                    _Render(liveValue),
                    _IsImmutable(property.Name, desiredValue, liveValue)
                )
            );
        }

        return divergences;
    }

    /// <summary>
    /// Returns the subjects this client requires that the live stream does not cover. Extra subjects on the live
    /// stream are never reported: sibling consumer groups and earlier deployments legitimately contribute their
    /// own, and treating those as drift would fail every multi-group deployment. Coverage uses NATS subject-match
    /// semantics, so a live <c>prefix.&gt;</c> wildcard covers an exact <c>prefix.foo</c>.
    /// </summary>
    public static IReadOnlyList<string> FindUncoveredSubjects(
        IEnumerable<string> requiredSubjects,
        ICollection<string>? liveSubjects
    )
    {
        var live = liveSubjects ?? [];

        return
        [
            .. requiredSubjects
                .Where(required => !live.Any(pattern => _CoversSubject(pattern, required)))
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// NATS subject matching: <c>*</c> matches exactly one token and <c>&gt;</c> matches one or more trailing
    /// tokens. An exact pattern matches only itself.
    /// </summary>
    public static bool Matches(string pattern, string subject)
    {
        var patternTokens = pattern.Split('.');
        var subjectTokens = subject.Split('.');

        for (var i = 0; i < patternTokens.Length; i++)
        {
            if (string.Equals(patternTokens[i], ">", StringComparison.Ordinal))
            {
                // '>' is only meaningful as the final token and needs at least one token to consume.
                return i == patternTokens.Length - 1 && subjectTokens.Length > i;
            }

            if (i >= subjectTokens.Length)
            {
                return false;
            }

            if (
                !string.Equals(patternTokens[i], "*", StringComparison.Ordinal)
                && !string.Equals(patternTokens[i], subjectTokens[i], StringComparison.Ordinal)
            )
            {
                return false;
            }
        }

        return patternTokens.Length == subjectTokens.Length;
    }

    /// <summary>
    /// Determines whether a live subject pattern covers a required subject pattern. Unlike ordinary NATS
    /// matching, wildcard tokens on the right are requirements: <c>orders.*</c> requires exactly one arbitrary
    /// token, while <c>orders.&gt;</c> requires one or more arbitrary trailing tokens and cannot be covered
    /// by <c>orders.*</c>.
    /// </summary>
    private static bool _CoversSubject(string pattern, string required)
    {
        var patternTokens = pattern.Split('.');
        var requiredTokens = required.Split('.');

        for (var i = 0; i < patternTokens.Length; i++)
        {
            if (string.Equals(patternTokens[i], ">", StringComparison.Ordinal))
            {
                return i == patternTokens.Length - 1 && requiredTokens.Length > i;
            }

            if (i >= requiredTokens.Length)
            {
                return false;
            }

            if (string.Equals(patternTokens[i], "*", StringComparison.Ordinal))
            {
                // A live '*' consumes exactly one token and cannot satisfy the open-ended cardinality
                // represented by a required trailing '>'.
                if (string.Equals(requiredTokens[i], ">", StringComparison.Ordinal))
                {
                    return false;
                }

                continue;
            }

            if (!string.Equals(patternTokens[i], requiredTokens[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return patternTokens.Length == requiredTokens.Length;
    }

    /// <summary>
    /// Classifies divergence against the NATS 2.9 update contract. One-way flags are special cases because
    /// enabling them is accepted, while disabling an enabled flag is rejected. Unlisted fields stay immutable.
    /// </summary>
    private static bool _IsImmutable(string field, object? desiredValue, object? liveValue)
    {
        if (field is nameof(StreamConfig.Sealed) or nameof(StreamConfig.DenyDelete) or nameof(StreamConfig.DenyPurge))
        {
            return !(desiredValue is false && liveValue is true);
        }

        return !_ReconcilableFields.Contains(field);
    }

    private static bool _StructurallyEqual(object? desiredValue, object? liveValue)
    {
        if (desiredValue is null || liveValue is null)
        {
            return false;
        }

        var desiredNode = JsonSerializer.SerializeToNode(desiredValue, desiredValue.GetType());
        var liveNode = JsonSerializer.SerializeToNode(liveValue, liveValue.GetType());

        return JsonNode.DeepEquals(desiredNode, liveNode);
    }

    /// <summary>
    /// Renders the divergences as an operator-facing message: what disagrees, what it costs, and the remedy that
    /// actually applies. The remedy is split because JetStream cannot converge every field — telling an operator
    /// to switch to <see cref="NatsStreamProvisioning.Reconcile"/> for a field the server refuses to change sends
    /// them into a loop of retrying an update that keeps failing.
    /// </summary>
    public static string ComposeDivergenceMessage(
        string streamName,
        IReadOnlyList<StreamDivergence> divergences,
        NatsStreamProvisioning mode
    )
    {
        var reconcilable = divergences.Where(d => !d.IsImmutable).ToList();
        var immutable = divergences.Where(d => d.IsImmutable).ToList();

        var message = new StringBuilder();

        message
            .Append(
                CultureInfo.InvariantCulture,
                $"NATS stream '{streamName}' does not match this application's configuration. "
            )
            .Append("Startup stopped instead of changing a stream this application may not own.");

        if (reconcilable.Count > 0)
        {
            message.AppendLine().AppendLine().Append("Differs, and can be reconciled in place:");

            foreach (var divergence in reconcilable)
            {
                message
                    .AppendLine()
                    .Append(
                        CultureInfo.InvariantCulture,
                        $"  - {divergence.Field}: this application wants '{divergence.Desired}', the stream has '{divergence.Actual}'"
                    );
            }

            if (
                reconcilable.Exists(d =>
                    string.Equals(d.Field, nameof(StreamConfig.Subjects), StringComparison.Ordinal)
                )
            )
            {
                message
                    .AppendLine()
                    .AppendLine()
                    .Append("A consumer bound to a subject the stream does not carry receives no messages, and ")
                    .Append("JetStream reports no error for it — which is why this stops startup rather than ")
                    .Append("letting the consumer run against a stream that cannot deliver to it.");
            }

            if (_ShouldOfferReconcile(mode))
            {
                message
                    .AppendLine()
                    .AppendLine()
                    .Append(
                        CultureInfo.InvariantCulture,
                        $"Set StreamProvisioning = NatsStreamProvisioning.{nameof(NatsStreamProvisioning.Reconcile)} "
                    )
                    .Append("to let this application write those values, or align the stream out of band.");
            }
        }

        if (immutable.Count > 0)
        {
            message.AppendLine().AppendLine().Append("Differs, and CANNOT be changed on a live stream:");

            foreach (var divergence in immutable)
            {
                message
                    .AppendLine()
                    .Append(
                        CultureInfo.InvariantCulture,
                        $"  - {divergence.Field}: this application wants '{divergence.Desired}', the stream has '{divergence.Actual}'"
                    );
            }

            message
                .AppendLine()
                .AppendLine()
                .Append("JetStream refuses these changes on an existing stream, so no StreamProvisioning mode can ")
                .Append("converge them. Recreate or migrate the stream to the wanted configuration, or change this ")
                .Append("application's StreamOptions to match the stream it is given.");
        }

        return message.ToString();
    }

    private static bool _ShouldOfferReconcile(NatsStreamProvisioning mode)
    {
        // Already reconciling: the run only reached this message because something else in the set is immutable,
        // so repeating the mode the operator already selected would read as a no-op instruction.
        return mode is not NatsStreamProvisioning.Reconcile;
    }

    private static string _Render(object? value)
    {
        return value switch
        {
            null => "(unset)",
            IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "(unset)",
        };
    }
}
