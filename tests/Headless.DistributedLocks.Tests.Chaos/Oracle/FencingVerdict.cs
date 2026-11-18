// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Tests.Oracle;

/// <summary>
/// The fencing oracle over a <see cref="FencingLedger"/> log. Under connection death or an asynchronous failover two
/// workers can legitimately believe they hold one resource, so mutual exclusion is a metric here, not a rule. What
/// must hold is the fencing guarantee the protected resource relies on:
/// <list type="bullet">
///   <item><see cref="AcceptedOrderRule"/>: per resource, accepted writes carry strictly increasing tokens.</item>
///   <item>
///   <see cref="IssuerRule"/>: the lock never grants a token at or below a token the resource had already accepted
///   when the acquire began. A token issued again, or a counter that rolled back, breaks it once a write landed.
///   It fails the run only where <see cref="FencingContract.IssuerRuleIsHard"/>; elsewhere each break is reported in
///   <see cref="Observations"/>.
///   </item>
/// </list>
/// </summary>
public sealed record FencingVerdict(
    IReadOnlyList<string> Violations,
    string? Evidence,
    IReadOnlyList<string> Observations,
    string? ObservationEvidence,
    int IssuerBreaks,
    int Grants,
    int AcceptedWrites,
    int RejectedWrites,
    int DuplicateGrantTokens,
    int RolledBackGrants
)
{
    /// <summary>Names the rule an accepted write broke by not exceeding the token accepted before it.</summary>
    public const string AcceptedOrderRule = "accepted-order";

    /// <summary>Names the rule a grant broke by carrying a token at or below one already accepted.</summary>
    public const string IssuerRule = "issuer";

    private const int _EvidenceSpan = 8;
    private const int _ListedViolations = 10;

    /// <summary>Judges <paramref name="events"/>, a log in <see cref="FencingEvent.Sequence"/> order.</summary>
    public static FencingVerdict Evaluate(IReadOnlyList<FencingEvent> events, FencingContract contract)
    {
        var violations = new List<string>();
        var observations = new List<string>();
        FencingEvent? firstBreak = null;
        FencingEvent? firstObserved = null;
        var issuerBreaks = 0;
        var lastAccepted = new Dictionary<string, long>(StringComparer.Ordinal);
        var granted = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        var highestGranted = new Dictionary<string, long>(StringComparer.Ordinal);
        int grants = 0,
            accepted = 0,
            rejected = 0,
            duplicates = 0,
            rolledBack = 0;

        foreach (var entry in events)
        {
            switch (entry.Kind)
            {
                case FencingEventKind.Granted:
                {
                    grants++;

                    if (entry.Token <= entry.HighWaterMark)
                    {
                        issuerBreaks++;
                        var issuerBreak = string.Create(
                            CultureInfo.InvariantCulture,
                            $"{IssuerRule}: {entry.Worker} was granted {entry.Resource} with token {entry.Token} after the resource had accepted token {entry.HighWaterMark} (#{entry.Sequence})"
                        );

                        if (contract.IssuerRuleIsHard)
                        {
                            violations.Add(issuerBreak);
                            firstBreak ??= entry;
                        }
                        else
                        {
                            observations.Add(issuerBreak);
                            firstObserved ??= entry;
                        }
                    }

                    var tokens = granted.TryGetValue(entry.Resource, out var set) ? set : granted[entry.Resource] = [];

                    if (!tokens.Add(entry.Token))
                    {
                        duplicates++;
                    }

                    if (entry.Token < highestGranted.GetValueOrDefault(entry.Resource))
                    {
                        rolledBack++;
                    }

                    highestGranted[entry.Resource] = Math.Max(
                        highestGranted.GetValueOrDefault(entry.Resource),
                        entry.Token
                    );
                    break;
                }
                case FencingEventKind.WriteAccepted:
                {
                    accepted++;

                    if (lastAccepted.TryGetValue(entry.Resource, out var previous) && entry.Token <= previous)
                    {
                        violations.Add(
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"{AcceptedOrderRule}: {entry.Resource} accepted token {entry.Token} from {entry.Worker} after token {previous} (#{entry.Sequence})"
                            )
                        );
                        firstBreak ??= entry;
                    }

                    lastAccepted[entry.Resource] = entry.Token;
                    break;
                }
                case FencingEventKind.WriteRejected:
                    rejected++;
                    break;
            }
        }

        _Cap(violations);
        _Cap(observations);

        return new FencingVerdict(
            violations,
            firstBreak is null ? null : _Slice(events, firstBreak),
            observations,
            firstObserved is null ? null : _Slice(events, firstObserved),
            issuerBreaks,
            grants,
            accepted,
            rejected,
            duplicates,
            rolledBack
        );
    }

    /// <summary>The counts in one line, with the ledger's holder metrics.</summary>
    public string Describe(FencingLedger ledger) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"grants {Grants}, writes accepted {AcceptedWrites}, stale writes rejected {RejectedWrites}, max concurrent holders {ledger.MaxConcurrentHolders}, overlapping holds {ledger.OverlappingHolds}, duplicate grant tokens {DuplicateGrantTokens}, rolled-back grants {RolledBackGrants}, issuer breaks {IssuerBreaks}"
        );

    // A broken issuer breaks on every grant after the first break, so list the first few and count the rest.
    private static void _Cap(List<string> lines)
    {
        if (lines.Count > _ListedViolations)
        {
            var more = lines.Count - _ListedViolations;
            lines.RemoveRange(_ListedViolations, more);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"and {more} more"));
        }
    }

    // The entries of the breaking resource around the first break, so the report shows the grant and the write it
    // collided with.
    private static string _Slice(IReadOnlyList<FencingEvent> events, FencingEvent firstBreak)
    {
        var sameResource = events
            .Where(entry => string.Equals(entry.Resource, firstBreak.Resource, StringComparison.Ordinal))
            .ToList();
        var index = sameResource.FindIndex(entry => entry.Sequence == firstBreak.Sequence);
        var from = Math.Max(0, index - _EvidenceSpan);
        var to = Math.Min(sameResource.Count, index + _EvidenceSpan + 1);

        return string.Join('\n', sameResource.Skip(from).Take(to - from));
    }
}
