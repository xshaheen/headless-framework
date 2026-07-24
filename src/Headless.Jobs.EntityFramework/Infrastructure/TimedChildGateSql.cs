// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// U5/KTD3: builds the native-SQL fragment of the timed-descendant claim gate for the PostgreSQL and SQL Server
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
    /// Returns a leading <c>AND (...)</c> clause for a fallback candidate <c>WHERE</c>. <paramref name="rootAlias"/>
    /// is the candidate row's alias; the parent is read through a correlated <c>EXISTS</c> subquery. The clause is
    /// self-contained SQL structure built only from provider-delimited identifiers and enum-name literals.
    /// </summary>
    public static string Build(TimeJobRelationalMapping mapping, string rootAlias)
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

        var runCondition = $"{rootAlias}.{mapping.RunCondition}";
        var parentStatus = $"gate_parent.{mapping.Status}";

        // A NULL RunCondition is ungated (matches the in-memory and EF C#-null-semantics behavior). It needs its own
        // arm because SQL three-valued logic makes `NULL NOT IN (...)` evaluate to UNKNOWN — without this the row
        // would be rejected forever, contradicting the other two providers.
        return $"""
            AND ({rootAlias}.{mapping.ParentId} IS NULL
                 OR {runCondition} IS NULL
                 OR {runCondition} NOT IN ('{onSuccess}', '{onFailure}', '{onCancelled}', '{onFailureOrCancelled}', '{onAny}')
                 OR EXISTS (
                     SELECT 1
                     FROM {mapping.Table} AS gate_parent
                     WHERE gate_parent.{mapping.Id} = {rootAlias}.{mapping.ParentId}
                       AND (
                           ({runCondition} = '{onSuccess}' AND {parentStatus} IN ('{succeeded}', '{dueDone}'))
                           OR ({runCondition} = '{onFailure}' AND {parentStatus} = '{failed}')
                           OR ({runCondition} = '{onCancelled}' AND {parentStatus} = '{cancelled}')
                           OR ({runCondition} = '{onFailureOrCancelled}' AND {parentStatus} IN ('{failed}', '{cancelled}'))
                           OR ({runCondition} = '{onAny}' AND {parentStatus} IN ('{succeeded}', '{dueDone}', '{failed}', '{cancelled}'))
                       )
                 ))
            """;
    }
}
