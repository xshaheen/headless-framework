// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// The registered consumers one host starts consumer clients for. A consumer outside the filter stays registered, so
/// the host still publishes its messages and describes it, while another host without the filter consumes them.
/// </summary>
/// <remarks>
/// Every-instance consumers are never filtered and never store rows, so they are absent from
/// <see cref="ConsumedIdentities"/>. Callers read the filter through <see cref="Internal.IConsumerHostOwnership"/>, which
/// combines it with the runtime subscriptions attached to the host.
/// </remarks>
internal sealed class MessagingConsumeFilter
{
    /// <summary>The unfiltered host: every registered consumer consumes.</summary>
    public static readonly MessagingConsumeFilter All = new(consumed: null);

    private readonly FrozenSet<string>? _consumed;

    private MessagingConsumeFilter(string[]? consumed)
    {
        ConsumedIdentities = consumed;
        _consumed = consumed?.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The exact consumer identities this host consumes, in ordinal order, or <see langword="null"/> when the host
    /// consumes every identity. Storage providers bind it as a received-row pickup predicate.
    /// </summary>
    public IReadOnlyCollection<string>? ConsumedIdentities { get; }

    /// <summary>
    /// Whether this host starts a consumer client for <paramref name="consumer"/>. An every-instance consumer always
    /// starts.
    /// </summary>
    public bool Allows(ConsumerMetadata consumer) =>
        consumer.EveryInstance || _consumed?.Contains(consumer.ConsumerIdentity) != false;

    /// <summary>
    /// Resolves <c>ConsumeOnly</c> entries against the registered consumers. An entry is an exact identity or
    /// an <c>owner.*</c> pattern that matches every identity whose owner segment, the text before the first <c>.</c>,
    /// equals <c>owner</c>. An entry that matches only every-instance consumers does not count as matched, because
    /// those consumers run on every host and the entry would have no effect.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// An entry matches no registered consumer, or only every-instance consumers.
    /// </exception>
    public static MessagingConsumeFilter Create(
        IReadOnlyCollection<string> entries,
        IEnumerable<ConsumerMetadata> consumers
    )
    {
        if (entries.Count == 0)
        {
            return All;
        }

        var registered = consumers.DistinctBy(x => x.ConsumerIdentity, StringComparer.Ordinal).ToArray();
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var unmatched = new List<string>();
        var everyInstanceOnly = new List<string>();
        foreach (var entry in entries)
        {
            var competing = false;
            var everyInstance = false;
            foreach (var consumer in registered)
            {
                if (!_Matches(entry, consumer.ConsumerIdentity))
                {
                    continue;
                }

                if (consumer.EveryInstance)
                {
                    everyInstance = true;
                }
                else
                {
                    consumed.Add(consumer.ConsumerIdentity);
                    competing = true;
                }
            }

            if (competing)
            {
                continue;
            }

            (everyInstance ? everyInstanceOnly : unmatched).Add(entry);
        }

        var errors = new List<string>();
        if (unmatched.Count != 0)
        {
            errors.Add(
                $"Messaging ConsumeOnly entries match no registered consumer: {string.Join(", ", unmatched.Select(x => $"'{x}'"))}. "
                    + "Add the module that declares them, or remove the entries."
            );
        }

        if (everyInstanceOnly.Count != 0)
        {
            errors.Add(
                $"Messaging ConsumeOnly entries match only every-instance consumers: {string.Join(", ", everyInstanceOnly.Select(x => $"'{x}'"))}. "
                    + "Every-instance consumers run on every host regardless of ConsumeOnly, so these entries have no effect; remove them."
            );
        }

        if (errors.Count != 0)
        {
            throw new InvalidOperationException(string.Join(' ', errors));
        }

        return new([.. consumed.Order(StringComparer.Ordinal)]);
    }

    /// <summary>Checks one <c>ConsumeOnly</c> entry's shape when it is authored.</summary>
    /// <exception cref="ArgumentException">The entry is empty, has surrounding whitespace, or misplaces <c>*</c>.</exception>
    public static string ValidateEntry(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry) || !string.Equals(entry, entry.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A ConsumeOnly entry must be a consumer identity or an 'owner.*' pattern without surrounding whitespace.",
                nameof(entry)
            );
        }

        if (!entry.Contains('*', StringComparison.Ordinal))
        {
            return entry;
        }

        // Only the whole name segment may be a wildcard, so a pattern always names exactly one owner.
        var owner = entry.EndsWith(".*", StringComparison.Ordinal) ? entry.AsSpan(0, entry.Length - 2) : [];
        if (owner.IsEmpty || owner.IndexOfAny('.', '*') >= 0)
        {
            throw new ArgumentException(
                $"ConsumeOnly entry '{entry}' is not valid. A pattern must have the form 'owner.*', where the owner is "
                    + "the identity text before the first '.'.",
                nameof(entry)
            );
        }

        return entry;
    }

    private static bool _Matches(string entry, string identity)
    {
        if (!entry.EndsWith(".*", StringComparison.Ordinal))
        {
            return string.Equals(entry, identity, StringComparison.Ordinal);
        }

        var owner = entry.AsSpan(0, entry.Length - 2);
        var separator = identity.IndexOf('.', StringComparison.Ordinal);
        return separator > 0 && identity.AsSpan(0, separator).SequenceEqual(owner);
    }
}
