// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Ledger;

/// <summary>
/// Writes one line per scenario run, with its counts and verdict, to the test output and to <c>summary.md</c> in the
/// directory <see cref="ChaosProfile.SummaryDirectoryVariable"/> names, so a scheduled run leaves a report beside its
/// test results.
/// </summary>
public static class ChaosSummary
{
    private const string _Header = """
        | Database | Scenario | Profile | Result | Counts |
        | --- | --- | --- | --- | --- |

        """;

    private static readonly Lock _Lock = new();

    public static void Record(string database, string scenario, IReadOnlyList<string> violations, string counts)
    {
        var result = violations.Count == 0 ? "pass" : "FAIL: " + string.Join("; ", violations);
        var line = $"| {database} | {scenario} | {ChaosProfile.Current.Name} | {_Escape(result)} | {_Escape(counts)} |";

        TestContext.Current.TestOutputHelper?.WriteLine(line);

        if (Environment.GetEnvironmentVariable(ChaosProfile.SummaryDirectoryVariable) is not { Length: > 0 } directory)
        {
            return;
        }

        lock (_Lock)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "summary.md");

            if (!File.Exists(path))
            {
                File.WriteAllText(path, _Header);
            }

            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private static string _Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
