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
    public static void Record(
        string store,
        string scenario,
        IReadOnlyList<string> violations,
        string counts,
        string? evidence,
        Action<string>? output
    )
    {
        var settings = ChaosRunSettings.Current;
        var result = violations.Count == 0 ? "pass" : "FAIL: " + string.Join("; ", violations);
        var line = $"| {store} | {scenario} | {_Escape(settings.ToString())} | {_Escape(result)} | {_Escape(counts)} |";

        output?.Invoke(line);

        if (violations.Count > 0 && evidence is { Length: > 0 })
        {
            output?.Invoke(evidence);
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

            if (violations.Count > 0 && evidence is { Length: > 0 })
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
