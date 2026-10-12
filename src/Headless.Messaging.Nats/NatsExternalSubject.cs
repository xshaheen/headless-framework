// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Internal;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

/// <summary>A contract lane's external subject as declared, before startup validation.</summary>
/// <param name="Filter">The subject filter the lane consumes, which may contain wildcards.</param>
/// <param name="TenantTokenIndex">The subject token that holds the tenant, if any.</param>
/// <param name="HasPublishSelector">Whether a selector computes each publish subject.</param>
internal sealed record NatsExternalSubjectDeclaration(string Filter, int? TenantTokenIndex, bool HasPublishSelector);

/// <summary>What startup validation needs from a NATS contract config without knowing its message type.</summary>
internal interface INatsMessageConfig
{
    NatsExternalSubjectDeclaration? ExternalSubject { get; }

    bool HasSubjectShard { get; }
}

/// <summary>One validated external subject binding: a contract lane and the foreign subject filter it consumes.</summary>
internal sealed class NatsExternalBinding
{
    public NatsExternalBinding(MessageLane lane, string messageName, NatsExternalSubjectDeclaration declaration)
    {
        Lane = lane;
        MessageName = messageName;
        Filter = declaration.Filter;
        TenantTokenIndex = declaration.TenantTokenIndex;
        HasPublishSelector = declaration.HasPublishSelector;
        IsLiteral = NatsExternalSubjectRules.IsLiteral(Filter);
    }

    public MessageLane Lane { get; }

    /// <summary>The contract's message name as registered.</summary>
    public string MessageName { get; }

    public string Filter { get; }

    public int? TenantTokenIndex { get; }

    public bool HasPublishSelector { get; }

    /// <summary>Whether the filter has no wildcard, so it is also the subject a selector-less publish goes to.</summary>
    public bool IsLiteral { get; }

    /// <summary>
    /// Returns the subject a message goes to: the subject computed at publish-request creation, or the filter itself
    /// when it is literal and no selector is declared.
    /// </summary>
    /// <exception cref="InvalidOperationException">No valid subject can be determined.</exception>
    public string PublishSubject(string? computed)
    {
        if (!string.IsNullOrWhiteSpace(computed))
        {
            return NatsExternalSubjectRules.ValidatePublishSubject(computed, Filter, $"message '{MessageName}'");
        }

        return IsLiteral
            ? Filter
            : throw new InvalidOperationException(
                $"Message '{MessageName}' on the {Lane} lane is bound to the NATS wildcard subject '{Filter}' without "
                    + "PublishTo, so it is consume-only. Declare PublishTo to publish it."
            );
    }

    /// <summary>
    /// Reads the tenant from the subject a delivery arrived on, or the reason it cannot, when the binding takes its
    /// tenant from a subject token.
    /// </summary>
    public (string? Tenant, string? Error) Tenant(string subject)
    {
        if (TenantTokenIndex is not { } index)
        {
            return (null, null);
        }

        var tokens = subject.Split('.');
        if (index >= tokens.Length || tokens[index].Length == 0)
        {
            return (null, string.Create(CultureInfo.InvariantCulture, $"subject token {index} is missing"));
        }

        var tenant = tokens[index];

        return tenant.Length > MessageOptions.TenantIdMaxLength
            ? (
                null,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"subject token {index} is longer than {MessageOptions.TenantIdMaxLength} characters"
                )
            )
            : (tenant, null);
    }
}

/// <summary>NATS subject syntax rules for external subject filters and the subjects published to them.</summary>
internal static class NatsExternalSubjectRules
{
    // Subject spaces an external filter may not reach into: the derived Headless subjects (a stream there would collide
    // with the streams Headless derives), inboxes, JetStream's API, and system events.
    public static readonly string[] ReservedSubjects = ["headless.>", "_INBOX.>", "$JS.>", "$SYS.>"];

    /// <summary>
    /// Returns why <paramref name="filter"/> is not a valid NATS subject filter, or <see langword="null"/> when it is.
    /// </summary>
    public static string? FilterError(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return "it is empty";
        }

        if (_HasWhiteSpaceOrControl(filter))
        {
            return "it contains whitespace or a control character";
        }

        var tokens = filter.Split('.');
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Length == 0)
            {
                return "it has an empty token";
            }

            if (string.Equals(token, ">", StringComparison.Ordinal))
            {
                if (i != tokens.Length - 1)
                {
                    return "'>' is not its last token";
                }

                continue;
            }

            if (!string.Equals(token, "*", StringComparison.Ordinal) && token.AsSpan().IndexOfAny('*', '>') >= 0)
            {
                return $"token '{token}' mixes a wildcard with other characters";
            }
        }

        return null;
    }

    /// <summary>Whether a valid filter has no wildcard token.</summary>
    public static bool IsLiteral(string filter) => !filter.Split('.').Any(token => token is "*" or ">");

    /// <summary>Returns <paramref name="subject"/> when it is literal and matches <paramref name="filter"/>.</summary>
    /// <exception cref="InvalidOperationException">The subject is not literal or does not match the filter.</exception>
    /// <param name="subject">The computed subject.</param>
    /// <param name="filter">The binding's filter.</param>
    /// <param name="owner">Names what the subject was computed for, in the exception message.</param>
    public static string ValidatePublishSubject(string? subject, string filter, string owner)
    {
        if (
            string.IsNullOrWhiteSpace(subject)
            || _HasWhiteSpaceOrControl(subject)
            || subject.AsSpan().IndexOfAny('*', '>') >= 0
            || subject.Split('.').Any(string.IsNullOrEmpty)
        )
        {
            throw new InvalidOperationException(
                $"The NATS subject computed for {owner} must be literal: no '*' or '>', no empty token, "
                    + "and no whitespace or control characters."
            );
        }

        if (!NatsStreamReconciliation.Matches(filter, subject))
        {
            throw new InvalidOperationException(
                $"The NATS subject '{subject}' computed for {owner} does not match its external subject "
                    + $"filter '{filter}'."
            );
        }

        return subject;
    }

    private static bool _HasWhiteSpaceOrControl(string value)
    {
        foreach (var character in value.AsSpan())
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The external subject bindings of every message contract, validated once at startup before any broker I/O: every
/// NATS component that resolves a stream, a filter, or a publish subject depends on it, so the first one resolved fails
/// the host on an invalid binding.
/// </summary>
internal sealed class NatsExternalSubjects
{
    /// <summary>No contract binds an external subject.</summary>
    public static NatsExternalSubjects Empty { get; } = new([]);

    private readonly Dictionary<(MessageLane Lane, string Name), NatsExternalBinding> _bindings;

    private NatsExternalSubjects(Dictionary<(MessageLane Lane, string Name), NatsExternalBinding> bindings)
    {
        _bindings = bindings;
    }

    public IReadOnlyCollection<NatsExternalBinding> All => _bindings.Values;

    /// <summary>Returns the binding of a contract lane, or <see langword="null"/> when it uses its derived subject.</summary>
    public NatsExternalBinding? Find(MessageLane lane, string messageName) =>
        _bindings.GetValueOrDefault((lane, messageName));

    /// <summary>Collects and validates the external subject bindings of <paramref name="routes"/>.</summary>
    /// <exception cref="InvalidOperationException">A binding is invalid; the message names the contract, lane, and filter.</exception>
    public static NatsExternalSubjects Create(IEnumerable<MessageMetadata> routes, NatsStreamCatalog streams)
    {
        Argument.IsNotNull(routes);
        Argument.IsNotNull(streams);

        var bindings = new Dictionary<(MessageLane Lane, string Name), NatsExternalBinding>();

        foreach (var route in routes)
        {
            var config = route.ProviderConfigs.Values.OfType<INatsMessageConfig>().FirstOrDefault();
            if (config?.ExternalSubject is not { } declaration)
            {
                continue;
            }

            var lane = route.Route.Lane;
            var name = route.Route.MessageName;
            var binding = new NatsExternalBinding(lane, name, declaration);

            if (bindings.TryGetValue((lane, name), out var existing))
            {
                if (!string.Equals(existing.Filter, binding.Filter, StringComparison.Ordinal))
                {
                    throw _Invalid(binding, $"another declaration of it binds '{existing.Filter}'");
                }

                continue;
            }

            _Validate(binding, config, streams);
            bindings.Add((lane, name), binding);
        }

        // One delivery must map to one contract, so no two contracts on a lane may share a subject.
        var all = bindings.Values.ToList();
        for (var i = 0; i < all.Count; i++)
        {
            for (var j = i + 1; j < all.Count; j++)
            {
                if (all[i].Lane == all[j].Lane && NatsStreamReconciliation.Overlaps(all[i].Filter, all[j].Filter))
                {
                    throw _Invalid(
                        all[j],
                        $"it overlaps '{all[i].Filter}', the external subject of message '{all[i].MessageName}' on the "
                            + "same lane, so a delivery could not tell which contract it belongs to"
                    );
                }
            }
        }

        return new NatsExternalSubjects(bindings);
    }

    private static void _Validate(NatsExternalBinding binding, INatsMessageConfig config, NatsStreamCatalog streams)
    {
        if (NatsExternalSubjectRules.FilterError(binding.Filter) is { } error)
        {
            throw _Invalid(binding, $"it is not a valid NATS subject filter: {error}");
        }

        if (
            NatsExternalSubjectRules.ReservedSubjects.FirstOrDefault(reserved =>
                NatsStreamReconciliation.Overlaps(reserved, binding.Filter)
            ) is
            { } reservedSubject
        )
        {
            throw _Invalid(binding, $"it overlaps the reserved subject space '{reservedSubject}'");
        }

        if (config.HasSubjectShard)
        {
            throw _Invalid(
                binding,
                "the lane also declares SubjectShard; an external subject replaces the derived one"
            );
        }

        if (
            binding.TenantTokenIndex is { } index
            && !binding.Filter.EndsWith('>')
            && index >= binding.Filter.Split('.').Length
        )
        {
            throw _Invalid(
                binding,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"TenantFromSubjectToken({index}) names a token the filter does not have"
                )
            );
        }

        // Ownership of a foreign subject space is stated, never inferred: a derived stream would silently capture the
        // application's subjects under a 'headless-' name.
        var covering = streams
            .Streams.Where(stream =>
                stream.Subjects.Any(pattern => NatsStreamReconciliation.Covers(pattern, binding.Filter))
            )
            .ToList();

        if (covering.Count != 1)
        {
            throw _Invalid(
                binding,
                covering.Count == 0
                    ? "no stream declared in NatsMessagingOptions.Streams (Own or Bind) covers it; Headless never derives "
                        + "a stream for a foreign subject"
                    : $"several declared streams cover it ({string.Join(", ", covering.Select(s => s.Name))})"
            );
        }

        // Each Bus consumer identity needs its own durable on the same filter, and JetStream refuses overlapping
        // consumers on a work-queue stream. A bound stream's retention is only known from the broker, so it is checked
        // there instead.
        if (
            binding.Lane is MessageLane.Bus
            && covering[0] is { Owned: true, Retention: StreamConfigRetention.Workqueue } owned
        )
        {
            throw _Invalid(
                binding,
                $"its covering stream '{owned.Name}' is owned with work-queue retention, which refuses the separate "
                    + "durable each Bus consumer group needs; use limits or interest retention"
            );
        }
    }

    private static InvalidOperationException _Invalid(NatsExternalBinding binding, string reason) =>
        new(
            $"NATS external subject '{binding.Filter}' of message '{binding.MessageName}' on the {binding.Lane} lane is "
                + $"invalid: {reason}."
        );
}
