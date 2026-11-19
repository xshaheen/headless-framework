// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// Builds the native-SQL fragment of the timed-descendant claim gate for the PostgreSQL and SQL Server
/// fallback claims, which select timed rows directly (<c>ExecutionTime IS NOT NULL</c>) and so must keep a timed
/// descendant out of the claim until its parent reached its matching terminal state. Mirrors the generic-EF
/// <c>WhereClaimableUnderParentTerminalGate</c> and the in-memory <c>_ParentGateAllowsClaim</c> — the three must
/// stay in lockstep. Enum member names come from <see langword="nameof"/> (compile-time literals, never runtime
/// values), matching the string-backed <c>RunCondition</c>/<c>JobStatus</c> column conversions, so they are inlined
/// rather than parameterized.
/// </summary>
internal static class TimedChildGateSql
{
    /// <summary>
    /// Returns a leading <c>AND (...)</c> clause for a fallback candidate <c>WHERE</c>. The candidate row's columns
    /// are unqualified, so the clause embeds in a claim whose table alias the dialect chooses. Each gated run
    /// condition reads the candidate's own parent through a correlated <c>EXISTS</c> keyed on the parent id, so a
    /// candidate costs one primary-key lookup however long the finished history grows. An uncorrelated
    /// <c>parent_id IN (SELECT id ... WHERE status IN (...))</c> lets PostgreSQL build a hashed subplan from a sequential
    /// scan of every terminal row on each claim. The subquery reads the parent through a derived table whose columns
    /// are renamed, so the unqualified parent-id column can only bind to the candidate row. The clause is
    /// self-contained SQL structure built only from provider-delimited identifiers and enum-name literals.
    /// </summary>
    public static string Build(TimeJobRelationalMapping mapping)
    {
        const string onSuccess = nameof(RunCondition.OnSuccess);
        const string onFailure = nameof(RunCondition.OnFailure);
        const string onCancelled = nameof(RunCondition.OnCancelled);
        const string onFailureOrCancelled = nameof(RunCondition.OnFailureOrCancelled);
        const string onAny = nameof(RunCondition.OnAnyCompletedStatus);
        const string succeeded = nameof(JobStatus.Succeeded);
        const string dueDone = nameof(JobStatus.DueDone);
        const string failed = nameof(JobStatus.Failed);
        const string cancelled = nameof(JobStatus.Cancelled);

        var runCondition = mapping.RunCondition;
        var onSuccessParent = parentExists($"'{succeeded}', '{dueDone}'");
        var onFailureParent = parentExists($"'{failed}'");
        var onCancelledParent = parentExists($"'{cancelled}'");
        var onFailureOrCancelledParent = parentExists($"'{failed}', '{cancelled}'");
        var onAnyParent = parentExists($"'{succeeded}', '{dueDone}', '{failed}', '{cancelled}'");

        // A NULL RunCondition is ungated (matches the in-memory and EF C#-null-semantics behavior). It needs its own
        // arm because SQL three-valued logic makes `NULL NOT IN (...)` evaluate to UNKNOWN — without this the row
        // would be rejected forever, contradicting the other two providers.
        return $"""
            AND ({mapping.ParentId} IS NULL
                 OR {runCondition} IS NULL
                 OR {runCondition} NOT IN ('{onSuccess}', '{onFailure}', '{onCancelled}', '{onFailureOrCancelled}', '{onAny}')
                 OR ({runCondition} = '{onSuccess}' AND {onSuccessParent})
                 OR ({runCondition} = '{onFailure}' AND {onFailureParent})
                 OR ({runCondition} = '{onCancelled}' AND {onCancelledParent})
                 OR ({runCondition} = '{onFailureOrCancelled}' AND {onFailureOrCancelledParent})
                 OR ({runCondition} = '{onAny}' AND {onAnyParent}))
            """;

        // gate_id and gate_status exist only inside the derived table, and the derived table has no parent-id column,
        // so the unqualified parent id resolves to the candidate row in every dialect without naming its alias.
        string parentExists(string statuses)
        {
            return $"EXISTS (SELECT 1 FROM (SELECT {mapping.Id} AS gate_id, {mapping.Status} AS gate_status FROM {mapping.Table}) AS gate_parent WHERE gate_parent.gate_id = {mapping.ParentId} AND gate_parent.gate_status IN ({statuses}))";
        }
    }
}
