// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

/// <summary>The names the NATS transport derives: subjects, streams, and durables.</summary>
/// <remarks>
/// Every name takes the host's isolation prefix (<c>Headless:Prefix</c>) in front, so environments that share one server
/// never share a subject, a stream, or a durable. A prefix holds no <c>.</c> or <c>_</c>, the separators used here, so it
/// ends at the first separator and two different prefixes can never produce one name. An empty prefix leaves every name
/// exactly as it is without one.
/// </remarks>
internal static class NatsPhysicalAddress
{
    private const string _SubjectRoot = "headless";

    // nats-server rejects durable names longer than JSMaxNameLen (255 bytes) and names containing whitespace, '.', '*',
    // '>', '/', or '\' (isValidAssetName): they become subject tokens and file names.
    private static readonly BusNameRules _BusDurableRules = new(
        maxLength: 255,
        isAllowed: static c => c is not ('.' or '*' or '>' or '/' or '\\')
    );

    public static string Subject(string hostPrefix, MessageLane lane, string logicalSubject) =>
        $"{SubjectRoot(hostPrefix)}.{_Lane(lane)}.{logicalSubject}";

    /// <summary>
    /// Returns the first tokens of every subject this host derives: <c>headless</c>, or <c>{prefix}.headless</c>. The
    /// prefix leads, rather than following <c>headless</c>, so no prefix can turn a derived subject into another lane's
    /// or into the reply space under <c>headless.reply.</c>.
    /// </summary>
    public static string SubjectRoot(string hostPrefix) =>
        hostPrefix.Length == 0 ? _SubjectRoot : $"{hostPrefix}.{_SubjectRoot}";

    public static string Stream(string hostPrefix, MessageLane lane, string normalizedLogicalStream) =>
        TransportNaming.NormalizeDistinct(_Prefixed(hostPrefix, $"headless-{_Lane(lane)}-{normalizedLogicalStream}"));

    /// <summary>
    /// Returns the name of a stream declared with <see cref="NatsStreamCatalog.Own"/>. Headless creates and reconciles
    /// it, so environments sharing a server each own their copy instead of rewriting one another's subjects.
    /// </summary>
    public static string OwnedDeclaredStream(string hostPrefix, string declaredName) =>
        _Prefixed(hostPrefix, declaredName);

    /// <summary>
    /// Returns a declared stream subject as this host must ask for it: a subject under the derived <c>headless.</c> root
    /// names subjects this host derives, so it moves under the prefixed root with them; any other subject belongs to a
    /// foreign producer and stays as declared.
    /// </summary>
    public static string DeclaredSubject(string hostPrefix, string subject) =>
        hostPrefix.Length > 0
        && (
            string.Equals(subject, _SubjectRoot, StringComparison.Ordinal)
            || subject.StartsWith(_SubjectRoot + ".", StringComparison.Ordinal)
        )
            ? $"{hostPrefix}.{subject}"
            : subject;

    /// <summary>
    /// Returns a Bus consumer identity's durable. A durable name is scoped to its stream, so the identity has one durable
    /// of this name on each stream it reads, filtering on every subject it consumes there: one pull loop per stream
    /// keeps the stream's order across message types.
    /// </summary>
    /// <remarks>
    /// The underscore keeps these names apart from the <see cref="LegacyBusDurable"/> names, which all start with
    /// <c>bus-</c>. With a dash, identity <c>a-b</c> would get the name identity <c>a</c> gave its durable for message
    /// <c>b</c>, and the first bind of <c>a</c> would take that durable over and delete it.
    /// </remarks>
    public static string BusDurable(string hostPrefix, string subscriptionName) =>
        BusNameBuilder.Build(_Prefixed(hostPrefix, $"bus_{subscriptionName}"), _BusDurableRules);

    /// <summary>
    /// Returns the per-subject Bus durable that releases before the per-stream <see cref="BusDurable"/> created. A
    /// consumer reads where these stopped when it first creates its per-stream durable, then deletes them.
    /// </summary>
    /// <remarks>
    /// The prefix applies here too: a bound stream can be shared by environments, and an unprefixed name would let a
    /// prefixed host find and delete another environment's durable.
    /// </remarks>
    public static string LegacyBusDurable(string hostPrefix, string subscriptionName, string logicalSubject) =>
        BusNameBuilder.Build(_Prefixed(hostPrefix, $"bus-{subscriptionName}-{logicalSubject}"), _BusDurableRules);

    /// <summary>
    /// Returns the Queue durable for one subject. Every host that consumes the message shares it, and hosts consume
    /// different messages of one stream, so a Queue durable stays per subject.
    /// </summary>
    public static string QueueDurable(string hostPrefix, string logicalSubject) =>
        TransportNaming.NormalizeDistinct(_Prefixed(hostPrefix, $"queue-{logicalSubject}"));

    // Bus streams use interest retention: a Bus durable starts at DeliverPolicy.New, so a message published before a
    // consumer exists never reaches it, and keeping it under limits retention would only cost storage.
    public static StreamConfigRetention Retention(MessageLane lane) =>
        lane switch
        {
            MessageLane.Bus => StreamConfigRetention.Interest,
            MessageLane.Queue => StreamConfigRetention.Workqueue,
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, message: null),
        };

    // Stream and durable names join the prefix with '_': NATS accepts it in both and a prefix never contains it. Durables
    // are scoped to their stream, but a stream declared with Bind can be shared by environments, so they take it too.
    private static string _Prefixed(string hostPrefix, string name) =>
        hostPrefix.Length == 0 ? name : $"{hostPrefix}_{name}";

    private static string _Lane(MessageLane lane) =>
        lane switch
        {
            MessageLane.Bus => "bus",
            MessageLane.Queue => "queue",
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, message: null),
        };
}
