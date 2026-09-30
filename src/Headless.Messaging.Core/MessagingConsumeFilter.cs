// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// The registered consumers one host starts consumer clients for. A consumer outside the filter stays registered, so
/// the host still publishes its messages and describes it, while another host without the filter consumes them.
/// </summary>
internal sealed class MessagingConsumeFilter
{
    /// <summary>The unfiltered host: every registered consumer consumes.</summary>
    public static readonly MessagingConsumeFilter All = new(consumed: null);

    private readonly FrozenSet<string>? _consumed;

    private MessagingConsumeFilter(FrozenSet<string>? consumed)
    {
        _consumed = consumed;
    }

    /// <summary>Whether this host starts a consumer client for the consumer with <paramref name="identity"/>.</summary>
    public bool Allows(string identity) => _consumed?.Contains(identity) != false;

    /// <summary>
    /// Resolves <c>ConsumeOnly</c> entries against the registered consumer identities. An entry is an exact identity or
    /// an <c>owner.*</c> pattern that matches every identity whose owner segment, the text before the first <c>.</c>,
    /// equals <c>owner</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">An entry matches no registered consumer.</exception>
    public static MessagingConsumeFilter Create(IReadOnlyCollection<string> entries, IEnumerable<string> identities)
    {
        if (entries.Count == 0)
        {
            return All;
        }

        var registered = identities.Distinct(StringComparer.Ordinal).ToArray();
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var unmatched = new List<string>();
        foreach (var entry in entries)
        {
            var matched = false;
            foreach (var identity in registered)
            {
                if (_Matches(entry, identity))
                {
                    consumed.Add(identity);
                    matched = true;
                }
            }

            if (!matched)
            {
                unmatched.Add(entry);
            }
        }

        if (unmatched.Count != 0)
        {
            throw new InvalidOperationException(
                $"Messaging ConsumeOnly entries match no registered consumer: {string.Join(", ", unmatched.Select(x => $"'{x}'"))}. "
                    + "Add the module that declares them, or remove the entries."
            );
        }

        return new(consumed.ToFrozenSet(StringComparer.Ordinal));
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
