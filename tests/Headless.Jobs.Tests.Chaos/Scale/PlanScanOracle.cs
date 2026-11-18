// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Tests.Scale;

/// <summary>One statement the store ran, with the plan it ran under.</summary>
public sealed record CapturedPlan(string QueryText, string Plan);

/// <summary>
/// Finds the full scans of a jobs table in captured plans: a PostgreSQL <c>Seq Scan</c>, or a SQL Server
/// <c>Table Scan</c> or <c>Clustered Index Scan</c>. Once a table holds a long finished history, such a scan reads the
/// whole history on every pass, which is the cost the indexes exist to avoid.
/// </summary>
public static partial class PlanScanOracle
{
    /// <summary>The statements the scenario itself runs carry this marker, so their plans are never judged.</summary>
    public const string ProbeMarker = "chaos-probe";

    /// <summary>
    /// Splits a PostgreSQL server log into the plans <c>auto_explain</c> wrote (text format): each entry starts at a
    /// <c>duration: ... plan:</c> line and continues over the tab-indented lines after it.
    /// </summary>
    public static IReadOnlyList<CapturedPlan> ParseAutoExplainLog(string log)
    {
        List<CapturedPlan> plans = [];
        List<string>? block = null;

        foreach (var line in log.Split('\n'))
        {
            if (
                line.Contains("duration:", StringComparison.Ordinal)
                && line.TrimEnd().EndsWith("plan:", StringComparison.Ordinal)
            )
            {
                _Flush(plans, block);
                block = [];

                continue;
            }

            if (block is not null && line.StartsWith('\t'))
            {
                block.Add(line.Trim());

                continue;
            }

            _Flush(plans, block);
            block = null;
        }

        _Flush(plans, block);

        return plans;
    }

    /// <summary>The <c>Seq Scan</c> nodes on <paramref name="tables"/> in a PostgreSQL text plan, alias included.</summary>
    public static IReadOnlyList<string> PostgreSqlScans(CapturedPlan plan, IReadOnlyCollection<string> tables)
    {
        return
        [
            .. _SeqScan
                .Matches(plan.Plan)
                .Where(match => tables.Contains(match.Groups["table"].Value, StringComparer.Ordinal))
                .Select(match => match.Value.Trim()),
        ];
    }

    /// <summary>The <c>Table Scan</c> and <c>Clustered Index Scan</c> operators on <paramref name="tables"/> in a showplan.</summary>
    public static IReadOnlyList<string> SqlServerScans(CapturedPlan plan, IReadOnlyCollection<string> tables)
    {
        var document = XDocument.Parse(plan.Plan);

        return
        [
            .. document
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "RelOp", StringComparison.Ordinal))
                .Select(relOp =>
                    (
                        Operator: (string?)relOp.Attribute("PhysicalOp"),
                        Table: relOp
                            .Elements()
                            .Where(x => x.Name.LocalName is "TableScan" or "IndexScan")
                            .Elements()
                            .Where(x => string.Equals(x.Name.LocalName, "Object", StringComparison.Ordinal))
                            .Select(x => ((string?)x.Attribute("Table"))?.Trim('[', ']'))
                            .FirstOrDefault()
                    )
                )
                .Where(x =>
                    x.Operator is "Table Scan" or "Clustered Index Scan"
                    && x.Table is not null
                    && tables.Contains(x.Table, StringComparer.Ordinal)
                )
                .Select(x => $"{x.Operator} on {x.Table}"),
        ];
    }

    /// <summary>The violations of a capture: every full scan, named with the statement that ran it.</summary>
    public static IReadOnlyList<string> Violations(
        IReadOnlyList<CapturedPlan> plans,
        Func<CapturedPlan, IReadOnlyList<string>> scans
    )
    {
        return
        [
            .. plans
                .Where(plan => !plan.QueryText.Contains(ProbeMarker, StringComparison.Ordinal))
                .SelectMany(plan => scans(plan).Select(scan => $"{scan} in: {Shorten(plan.QueryText)}"))
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>A statement on one line, cut to a length a report row can carry.</summary>
    public static string Shorten(string sql)
    {
        var flat = _Whitespace.Replace(sql, " ").Trim();

        return flat.Length <= 240 ? flat : $"{flat[..240]}…";
    }

    private static void _Flush(List<CapturedPlan> plans, List<string>? block)
    {
        if (block is not { Count: > 0 })
        {
            return;
        }

        var query = block.FirstOrDefault(x => x.StartsWith("Query Text:", StringComparison.Ordinal));
        plans.Add(new CapturedPlan(query?["Query Text:".Length..].Trim() ?? string.Empty, string.Join('\n', block)));
    }

    [GeneratedRegex(
        @"(?:Parallel )?Seq Scan on (?:\w+\.)?(?<table>\w+)(?: \w+)?",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex _SeqScan { get; }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex _Whitespace { get; }
}
