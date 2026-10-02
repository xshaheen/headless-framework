// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

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
    /// are unqualified, so the clause embeds in a claim whose table alias the dialect chooses; each gated run
    /// condition reads the parent through its own <c>IN</c> subquery, so no column inside a subquery has to name the
    /// candidate row. The clause is self-contained SQL structure built only from provider-delimited identifiers and
    /// enum-name literals.
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
        var onSuccessParent = parentIn($"'{succeeded}', '{dueDone}'");
        var onFailureParent = parentIn($"'{failed}'");
        var onCancelledParent = parentIn($"'{cancelled}'");
        var onFailureOrCancelledParent = parentIn($"'{failed}', '{cancelled}'");
        var onAnyParent = parentIn($"'{succeeded}', '{dueDone}', '{failed}', '{cancelled}'");

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

        string parentIn(string statuses)
        {
            return $"{mapping.ParentId} IN (SELECT gate_parent.{mapping.Id} FROM {mapping.Table} AS gate_parent WHERE gate_parent.{mapping.Status} IN ({statuses}))";
        }
    }
}
