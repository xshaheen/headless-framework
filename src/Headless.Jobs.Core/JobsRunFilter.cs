// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// The set of registered jobs one host claims and executes. Persistence providers apply it as a predicate on every
/// claim, acquire, timed-out sweep, and next-occurrence query, so a filtered host never leases a row it will not run.
/// </summary>
/// <remarks>
/// Scheduling, cron seeding, and dashboards ignore the filter: a filtered-out job stays registered, so the host can
/// still enqueue it for another host to run, and seeding keeps its cron definition.
/// </remarks>
internal sealed class JobsRunFilter
{
    /// <summary>The unfiltered host: every registered job runs, and unregistered functions are still claimed.</summary>
    public static readonly JobsRunFilter All = new(runnableFunctions: null);

    private readonly FrozenSet<string>? _runnable;

    private JobsRunFilter(string[]? runnableFunctions)
    {
        RunnableFunctions = runnableFunctions;
        _runnable = runnableFunctions?.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The exact function names this host runs, in ordinal order, or <see langword="null"/> when the host runs every
    /// function. Relational providers bind it as a query parameter.
    /// </summary>
    public string[]? RunnableFunctions { get; }

    /// <summary>Whether this host restricts what it runs.</summary>
    public bool IsFiltered => _runnable is not null;

    /// <summary>Whether this host claims and executes <paramref name="function"/>.</summary>
    public bool Allows(string function) => _runnable?.Contains(function) != false;

    /// <summary>
    /// Resolves <c>RunOnly</c> entries against the registered job identities. An entry is an exact identity or an
    /// <c>owner.*</c> pattern that matches every identity whose owner segment, the text before the first <c>.</c>,
    /// equals <c>owner</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">An entry matches no registered job.</exception>
    public static JobsRunFilter Create(IReadOnlyCollection<string> entries, IEnumerable<string> registeredFunctions)
    {
        if (entries.Count == 0)
        {
            return All;
        }

        var registered = registeredFunctions.ToArray();
        var runnable = new HashSet<string>(StringComparer.Ordinal);
        var unmatched = new List<string>();
        foreach (var entry in entries)
        {
            var matched = false;
            foreach (var function in registered)
            {
                if (_Matches(entry, function))
                {
                    runnable.Add(function);
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
                $"Jobs RunOnly entries match no registered job: {string.Join(", ", unmatched.Select(x => $"'{x}'"))}. "
                    + "Add the module that declares them, or remove the entries."
            );
        }

        return new([.. runnable.Order(StringComparer.Ordinal)]);
    }

    /// <summary>Checks one <c>RunOnly</c> entry's shape when it is authored.</summary>
    /// <exception cref="ArgumentException">The entry is empty, has surrounding whitespace, or misplaces <c>*</c>.</exception>
    public static string ValidateEntry(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry) || !string.Equals(entry, entry.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A RunOnly entry must be a job identity or an 'owner.*' pattern without surrounding whitespace.",
                nameof(entry)
            );
        }

        var star = entry.IndexOf('*', StringComparison.Ordinal);
        if (star < 0)
        {
            return entry;
        }

        // Only the whole name segment may be a wildcard, so a pattern always names exactly one owner.
        var owner = entry.EndsWith(".*", StringComparison.Ordinal) ? entry[..^2] : null;
        if (
            owner is not { Length: > 0 }
            || owner.Contains('.', StringComparison.Ordinal)
            || owner.Contains('*', StringComparison.Ordinal)
        )
        {
            throw new ArgumentException(
                $"RunOnly entry '{entry}' is not valid. A pattern must have the form 'owner.*', where the owner is the "
                    + "identity text before the first '.'.",
                nameof(entry)
            );
        }

        return entry;
    }

    private static bool _Matches(string entry, string function)
    {
        if (!entry.EndsWith(".*", StringComparison.Ordinal))
        {
            return string.Equals(entry, function, StringComparison.Ordinal);
        }

        var owner = entry.AsSpan(0, entry.Length - 2);
        var separator = function.IndexOf('.', StringComparison.Ordinal);
        return separator > 0 && function.AsSpan(0, separator).SequenceEqual(owner);
    }
}
