// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Tests.Oracle;

/// <summary>
/// The fencing oracle over a <see cref="FencingLedger"/> log. Under connection death or an asynchronous failover two
/// workers can legitimately believe they hold one resource, so mutual exclusion is a metric here, not a rule. The rule
/// is the issuer's (<see cref="IssuerRuleName"/>): the lock never grants a token at or below a token the resource had
/// already accepted when the acquire began. A token issued again, or a counter that rolled back, breaks it once a
/// write landed. The provider's <see cref="FencingContract"/> decides whether a break fails the run or, where a
/// failover explains it, is reported in <see cref="Observations"/>.
/// </summary>
/// <remarks>
/// <see cref="StoreSelfCheck"/> is not a contract rule: the in-test store accepts only a higher token, so accepted
/// writes increase by construction, and the check only catches a broken harness store.
/// </remarks>
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
    /// <summary>Names the harness self-check an accepted write broke by not exceeding the token accepted before it.</summary>
    public const string StoreSelfCheck = "store-self-check";

    /// <summary>Names the rule a grant broke by carrying a token at or below one already accepted.</summary>
    public const string IssuerRuleName = "issuer";

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
        var failovers = _Failovers(events);
        var grantedAt = new Dictionary<(string Resource, long Token), long>();
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
                            $"{IssuerRuleName}: {entry.Worker} was granted {entry.Resource} with token {entry.Token} after the resource had accepted token {entry.HighWaterMark} (#{entry.Sequence})"
                        );

                        if (!_Excused(contract, failovers, grantedAt, entry))
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

                    grantedAt[(entry.Resource, entry.Token)] = entry.Sequence;
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
                                $"{StoreSelfCheck}: {entry.Resource} accepted token {entry.Token} from {entry.Worker} after token {previous} (#{entry.Sequence})"
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

    // A failover may excuse a reissue only when it overlapped the span from the grant of the accepted high-water token
    // to the offending grant: the lost INCR came before the promotion, and the reissue after the route moved. Both ends
    // of the failover count, because a grant reply in flight can be logged just after the failover started.
    private static bool _Excused(
        FencingContract contract,
        IReadOnlyList<(long Start, long End)> failovers,
        Dictionary<(string Resource, long Token), long> grantedAt,
        FencingEvent grant
    )
    {
        if (
            contract.Issuer == IssuerRule.Hard
            || !grantedAt.TryGetValue((grant.Resource, grant.HighWaterMark), out var highWaterGrant)
        )
        {
            return false;
        }

        return failovers.Any(failover => failover.Start < grant.Sequence && failover.End > highWaterGrant);
    }

    private static List<(long Start, long End)> _Failovers(IReadOnlyList<FencingEvent> events)
    {
        var failovers = new List<(long Start, long End)>();
        long? start = null;

        foreach (var entry in events)
        {
            if (entry.Kind == FencingEventKind.FailoverStarted)
            {
                start = entry.Sequence;
            }
            else if (entry.Kind == FencingEventKind.FailoverEnded && start is { } started)
            {
                failovers.Add((started, entry.Sequence));
                start = null;
            }
        }

        // A failover the run stopped in the middle of still spans to the end of the log.
        if (start is { } open)
        {
            failovers.Add((open, long.MaxValue));
        }

        return failovers;
    }

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
            .Where(entry =>
                string.Equals(entry.Resource, firstBreak.Resource, StringComparison.Ordinal)
                || string.Equals(entry.Resource, FencingLedger.AllResources, StringComparison.Ordinal)
            )
            .ToList();
        var index = sameResource.FindIndex(entry => entry.Sequence == firstBreak.Sequence);
        var from = Math.Max(0, index - _EvidenceSpan);
        var to = Math.Min(sameResource.Count, index + _EvidenceSpan + 1);

        return string.Join('\n', sameResource.Skip(from).Take(to - from));
    }
}
