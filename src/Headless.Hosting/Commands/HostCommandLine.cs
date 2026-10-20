// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;

namespace Headless.Hosting;

/// <summary>The argument parsing, exit codes, and report layout the host verbs share.</summary>
internal static class HostCommandLine
{
    public const int ExitSuccess = 0;
    public const int ExitFailure = 1;
    public const int ExitUsage = 2;

    /// <summary>
    /// Parses <paramref name="args"/>: an optional leading subcommand from <paramref name="subcommands"/>, then options
    /// from <paramref name="options"/> (alias to canonical name), each followed by one value, and switches from
    /// <paramref name="switches"/>, which take none; <c>--timeout</c> takes a whole number of seconds above zero, and
    /// <c>-h</c>/<c>--help</c> asks for the usage.
    /// </summary>
    public static bool TryParse(
        string verb,
        IReadOnlyList<string> args,
        IReadOnlyCollection<string> subcommands,
        IReadOnlyDictionary<string, string> options,
        out HostCommandArguments parsed,
        out string error,
        IReadOnlyCollection<string>? switches = null
    )
    {
        parsed = new HostCommandArguments();
        error = "";
        var index = 0;

        if (args.Count > 0 && !args[0].StartsWith('-'))
        {
            if (!subcommands.Contains(args[0], StringComparer.Ordinal))
            {
                error = $"Unknown {verb} command '{args[0]}'.";

                return false;
            }

            parsed.Subcommand = args[0];
            index = 1;
        }

        for (; index < args.Count; index++)
        {
            var flag = args[index];

            if (flag is "-h" or "--help")
            {
                parsed.Help = true;

                continue;
            }

            if (switches?.Contains(flag, StringComparer.Ordinal) == true)
            {
                parsed.Switches.Add(flag);

                continue;
            }

            var isTimeout = string.Equals(flag, "--timeout", StringComparison.Ordinal);

            if (!isTimeout && !options.ContainsKey(flag))
            {
                error = $"Unknown {verb} option '{flag}'.";

                return false;
            }

            if (index + 1 >= args.Count)
            {
                error = $"The {verb} option '{flag}' needs a value.";

                return false;
            }

            var value = args[++index];

            if (!isTimeout)
            {
                var name = options[flag];

                if (!parsed.Values.TryGetValue(name, out var values))
                {
                    parsed.Values[name] = values = [];
                }

                values.Add(value);

                continue;
            }

            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
            {
                error = $"The {verb} option '--timeout' needs a whole number of seconds above zero, not '{value}'.";

                return false;
            }

            parsed.Timeout = TimeSpan.FromSeconds(seconds);
        }

        return true;
    }

    /// <summary>
    /// Writes one aligned line per row: every column padded to its widest value (the duration column right-aligned),
    /// then the message when there is one, then each detail indented. Lines end with <c>\n</c> on every platform, so a
    /// deploy log or a grep reads each row whole.
    /// </summary>
    public static string FormatTable(IReadOnlyList<HostCommandRow> rows, int rightAlignedColumn = -1)
    {
        if (rows.Count == 0)
        {
            return "";
        }

        var columnCount = rows.Max(static r => r.Columns.Count);
        var widths = new int[columnCount];

        foreach (var row in rows)
        {
            for (var column = 0; column < row.Columns.Count; column++)
            {
                widths[column] = Math.Max(widths[column], row.Columns[column].Length);
            }
        }

        var builder = new StringBuilder();

        foreach (var row in rows)
        {
            for (var column = 0; column < row.Columns.Count; column++)
            {
                if (column > 0)
                {
                    builder.Append("  ");
                }

                var value = row.Columns[column];
                var last = column == row.Columns.Count - 1 && row.Message.Length == 0;
                builder.Append(
                    column == rightAlignedColumn ? value.PadLeft(widths[column])
                    : last ? value
                    : value.PadRight(widths[column])
                );
            }

            if (row.Message.Length > 0)
            {
                builder.Append("  ").Append(SingleLine(row.Message));
            }

            builder.Append('\n');

            foreach (var detail in row.Details)
            {
                builder.Append("    ").Append(SingleLine(detail)).Append('\n');
            }
        }

        return builder.ToString();
    }

    /// <summary>Formats a duration the way every verb prints it.</summary>
    public static string Duration(TimeSpan duration) =>
        string.Create(CultureInfo.InvariantCulture, $"{duration.TotalMilliseconds:0}ms");

    /// <summary>Joins a multi-line text onto one line.</summary>
    public static string SingleLine(string text) =>
        text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal)
            ? string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            : text;

    /// <summary>Writes a usage error and the usage line to <paramref name="stderr"/>, and returns the usage exit code.</summary>
    public static async Task<int> UsageErrorAsync(
        TextWriter stderr,
        string error,
        string usage,
        CancellationToken cancellationToken
    )
    {
        await stderr.WriteLineAsync(error).ConfigureAwait(false);
        await stderr.WriteLineAsync(usage).ConfigureAwait(false);
        await stderr.FlushAsync(cancellationToken).ConfigureAwait(false);

        return ExitUsage;
    }

    /// <summary>Writes <paramref name="text"/> to <paramref name="writer"/> and flushes it.</summary>
    public static async Task WriteAsync(TextWriter writer, string text, CancellationToken cancellationToken)
    {
        await writer.WriteAsync(text).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>What a host verb's arguments asked for.</summary>
internal sealed class HostCommandArguments
{
    public string? Subcommand { get; set; }

    public bool Help { get; set; }

    public TimeSpan? Timeout { get; set; }

    public Dictionary<string, List<string>> Values { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Switches { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<string> ValuesOf(string name) => Values.TryGetValue(name, out var values) ? values : [];
}

/// <summary>One report line: its columns, an optional message, and indented detail lines after it.</summary>
internal sealed record HostCommandRow(IReadOnlyList<string> Columns, string Message, IReadOnlyList<string> Details);
