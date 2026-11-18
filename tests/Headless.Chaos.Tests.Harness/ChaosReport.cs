// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>
/// Writes one line per scenario run, with its counts and verdict, to the test output and to <c>summary.md</c> in the
/// directory <see cref="ChaosRunSettings.SummaryDirectoryVariable"/> names, so a scheduled run leaves a report beside
/// its test results. A failed scenario also writes the evidence that broke its oracle, such as the slice of the
/// side-effect log, into <c>failures.md</c> next to the summary.
/// </summary>
public static class ChaosReport
{
    private const string _Header = """
        | Store | Scenario | Run | Result | Counts |
        | --- | --- | --- | --- | --- |

        """;

    private static readonly Lock _Lock = new();

    /// <summary>Records one scenario run.</summary>
    /// <param name="store">The store or stack the scenario ran on, such as <c>PostgreSQL</c>.</param>
    /// <param name="scenario">The scenario's short name.</param>
    /// <param name="violations">The oracle's violations; empty for a pass.</param>
    /// <param name="counts">The counts the scenario measured, in one line.</param>
    /// <param name="evidence">On a failure, the log slice or rows that broke the oracle.</param>
    /// <param name="output">The test's output sink; the summary line and the evidence also go there.</param>
    /// <param name="negativeControl">
    /// Whether the run sabotaged the stack on purpose, so the oracle must report violations: the row then reads
    /// <c>pass</c> when it did and <c>FAIL</c> when the sabotage slipped through, and the summary never shows a red row
    /// for a control that worked.
    /// </param>
    /// <param name="quarantinedBy">
    /// The issue a known defect is quarantined against, when every violation is that defect's: the row reads
    /// <c>quarantined</c> instead of <c>FAIL</c>, and its evidence still goes to <c>failures.md</c>.
    /// </param>
    public static void Record(
        string store,
        string scenario,
        IReadOnlyList<string> violations,
        string counts,
        string? evidence,
        Action<string>? output,
        bool negativeControl = false,
        string? quarantinedBy = null
    )
    {
        var settings = ChaosRunSettings.Current;
        var caught = string.Join("; ", violations);
        var (result, failed) = (negativeControl, violations.Count, quarantinedBy) switch
        {
            (true, 0, _) => ("FAIL: the sabotage was not caught", true),
            (true, _, _) => ("pass (control caught: " + caught + ")", false),
            (false, 0, _) => ("pass", false),
            (false, _, { Length: > 0 } issue) => ($"quarantined {issue}: {caught}", true),
            _ => ("FAIL: " + caught, true),
        };
        var line = $"| {store} | {scenario} | {_Escape(settings.ToString())} | {_Escape(result)} | {_Escape(counts)} |";

        output?.Invoke(line);

        var withEvidence = failed && evidence is { Length: > 0 };

        if (withEvidence)
        {
            output?.Invoke(evidence!);
        }

        if (Environment.GetEnvironmentVariable(ChaosRunSettings.SummaryDirectoryVariable) is not { Length: > 0 } dir)
        {
            return;
        }

        lock (_Lock)
        {
            Directory.CreateDirectory(dir);
            var summary = Path.Combine(dir, "summary.md");

            if (!File.Exists(summary))
            {
                File.WriteAllText(summary, _Header);
            }

            File.AppendAllText(summary, line + Environment.NewLine);

            if (withEvidence)
            {
                File.AppendAllText(
                    Path.Combine(dir, "failures.md"),
                    $"## {store} · {scenario} ({settings})\n\n```text\n{evidence}\n```\n\n"
                );
            }
        }
    }

    private static string _Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
