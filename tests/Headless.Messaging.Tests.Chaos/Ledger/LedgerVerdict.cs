// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Tests.Ledger;

/// <summary>
/// What a ledger shows about delivery: which accepted messages never ran, which ran more than once, and which ran
/// without the stack ever accepting them. A scenario judges its pass threshold on these counts.
/// </summary>
/// <param name="Accepted">Messages the stack accepted.</param>
/// <param name="Rejected">Publishes the stack refused; the caller saw the failure, so they are not owed a run.</param>
/// <param name="Completions">Handler runs that completed, duplicates included.</param>
/// <param name="Lost">Accepted messages whose handler never completed.</param>
/// <param name="Duplicated">Messages whose handler completed more than once.</param>
/// <param name="Unexpected">
/// Messages that ran although their publish was refused: the write committed but its reply was lost. Reported, never a
/// failure, because the caller cannot tell a lost reply from a failed write.
/// </param>
/// <param name="Exhausted">Messages the framework gave up as terminally failed.</param>
public sealed record LedgerVerdict(
    int Accepted,
    int Rejected,
    int Completions,
    IReadOnlyList<string> Lost,
    IReadOnlyList<string> Duplicated,
    IReadOnlyList<string> Unexpected,
    IReadOnlyList<string> Exhausted
)
{
    private const int _SampleSize = 10;

    public static LedgerVerdict Evaluate(LedgerSnapshot snapshot)
    {
        var entries = snapshot.Entries.Values.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToList();

        return new LedgerVerdict(
            Accepted: entries.Count(entry => entry.Accepted),
            Rejected: entries.Sum(entry => entry.Rejections),
            Completions: entries.Sum(entry => entry.Completed),
            Lost: [.. entries.Where(entry => entry.Accepted && entry.Completed == 0).Select(entry => entry.Key)],
            Duplicated: [.. entries.Where(entry => entry.Completed > 1).Select(entry => entry.Key)],
            Unexpected: [.. entries.Where(entry => !entry.Accepted && entry.Completed > 0).Select(entry => entry.Key)],
            Exhausted: [.. entries.Where(entry => entry.Exhausted > 0).Select(entry => entry.Key)]
        );
    }

    /// <summary>The share of accepted messages that ran more than once.</summary>
    public double DuplicateRatio => Accepted == 0 ? 0 : (double)Duplicated.Count / Accepted;

    /// <summary>
    /// The failures of an exactly-once threshold: every accepted message ran once, none twice, and none was given up.
    /// </summary>
    public IReadOnlyList<string> ExactlyOnceViolations() => [.. LossViolations(), .. _DuplicateViolations()];

    /// <summary>
    /// The failures of a no-loss threshold that tolerates duplicates up to <paramref name="maxDuplicateRatio"/> of the
    /// accepted messages.
    /// </summary>
    public IReadOnlyList<string> NoLossViolations(double maxDuplicateRatio)
    {
        List<string> violations = [.. LossViolations()];

        if (DuplicateRatio >= maxDuplicateRatio && Duplicated.Count > 0)
        {
            violations.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Duplicated.Count} of {Accepted} message(s) ran more than once ({DuplicateRatio:P2}), at or above the {maxDuplicateRatio:P2} limit: {_Sample(Duplicated)}"
                )
            );
        }

        return violations;
    }

    /// <summary>
    /// The failures of a load scenario's acceptance floor. The loss checks judge only accepted messages, so a run that
    /// accepted nothing would pass them having exercised nothing; this fails it instead.
    /// </summary>
    /// <param name="attempted">How many publishes the scenario attempted.</param>
    /// <param name="minimumShare">The share of <paramref name="attempted"/> that must be accepted, from 0 to 1.</param>
    public IEnumerable<string> AcceptanceViolations(int attempted, double minimumShare)
    {
        if (attempted == 0)
        {
            yield return "no publish was attempted";
            yield break;
        }

        if (Accepted == 0)
        {
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"none of {attempted} attempted publish(es) was accepted"
            );
            yield break;
        }

        if (Accepted < attempted * minimumShare)
        {
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"only {Accepted} of {attempted} attempted publish(es) were accepted, under the {minimumShare:P0} floor"
            );
        }
    }

    /// <summary>The counts in one line, for the run summary and the test output.</summary>
    public string Describe() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"accepted {Accepted}, rejected {Rejected}, completions {Completions}, lost {Lost.Count}, duplicated {Duplicated.Count} ({DuplicateRatio:P2}), unexpected {Unexpected.Count}, exhausted {Exhausted.Count}"
        );

    /// <summary>The failures of a no-loss threshold that counts duplicates without judging them.</summary>
    public IEnumerable<string> LossViolations()
    {
        if (Lost.Count > 0)
        {
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"{Lost.Count} accepted message(s) never ran: {_Sample(Lost)}"
            );
        }

        if (Exhausted.Count > 0)
        {
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"{Exhausted.Count} message(s) were given up as terminally failed: {_Sample(Exhausted)}"
            );
        }
    }

    private IEnumerable<string> _DuplicateViolations()
    {
        if (Duplicated.Count > 0)
        {
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"{Duplicated.Count} message(s) ran more than once: {_Sample(Duplicated)}"
            );
        }
    }

    private static string _Sample(IReadOnlyList<string> keys) =>
        string.Join(", ", keys.Take(_SampleSize)) + (keys.Count > _SampleSize ? ", ..." : string.Empty);
}
