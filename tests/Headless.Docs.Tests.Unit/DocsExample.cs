// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;

namespace Tests;

/// <summary>A fenced <c>csharp</c> block in a <c>docs/llms</c> guide.</summary>
/// <param name="Guide">Guide file name, such as <c>messaging.md</c>.</param>
/// <param name="Line">1-based line of the first code line in the guide.</param>
/// <param name="Code">Block contents with the fence indentation removed.</param>
/// <param name="Section">Nearest preceding <c>##</c> heading, used to pick one package from a conflict group.</param>
/// <param name="Marker">The <c>&lt;!-- example: ... --&gt;</c> marker directly above the fence, if any.</param>
public sealed record DocsExample(string Guide, int Line, string Code, string Section, string? Marker)
{
    /// <summary>Marks the next fenced block as an intentional fragment that cannot compile on its own.</summary>
    public const string FragmentMarker = "<!-- example: fragment -->";

    /// <summary>
    /// Marks the next fenced block as a self-contained host setup that also starts: its statements run against a
    /// real builder and the host is started and stopped, so startup validation failures surface.
    /// </summary>
    public const string BootMarker = "<!-- example: boot -->";

    public bool IsFragment => string.Equals(Marker, FragmentMarker, StringComparison.Ordinal);

    public bool Boots => string.Equals(Marker, BootMarker, StringComparison.Ordinal);

    public string Id => $"{Guide}:{Line}";

    public override string ToString() => Id;
}

public static partial class DocsExamples
{
    public static string RepositoryRoot { get; } = _FindRepositoryRoot();

    public static string GuidesDirectory { get; } = Path.Combine(RepositoryRoot, "docs", "llms");

    public static IReadOnlyList<DocsExample> All { get; } = _LoadAll();

    public static IReadOnlyList<DocsExample> Parse(string guide, string[] lines)
    {
        var examples = new List<DocsExample>();
        var section = string.Empty;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                section = line[3..].Trim();
                continue;
            }

            var open = _OpenFence.Match(line);

            if (!open.Success)
            {
                continue;
            }

            var indent = open.Groups["indent"].Value.Length;
            var start = i + 1;
            var end = start;

            while (end < lines.Length && !_CloseFence.IsMatch(lines[end]))
            {
                end++;
            }

            var code = string.Join(
                '\n',
                lines[start..end].Select(l => l.Length >= indent ? l[indent..] : l.TrimStart())
            );

            examples.Add(new DocsExample(guide, start + 1, code, section, _GetMarker(lines, i)));
            i = end;
        }

        return examples;
    }

    private static string? _GetMarker(string[] lines, int fenceIndex)
    {
        for (var i = fenceIndex - 1; i >= 0; i--)
        {
            var text = lines[i].Trim();

            if (text.Length == 0)
            {
                continue;
            }

            return text.StartsWith("<!-- example:", StringComparison.Ordinal) ? text : null;
        }

        return null;
    }

    private static List<DocsExample> _LoadAll()
    {
#pragma warning disable MA0045 // False positive: this runs once in a static initializer, which cannot await.
        return
        [
            .. Directory
                .EnumerateFiles(GuidesDirectory, "*.md")
                .Order(StringComparer.Ordinal)
                .SelectMany(path => Parse(Path.GetFileName(path), File.ReadAllLines(path))),
        ];
#pragma warning restore MA0045
    }

    private static string _FindRepositoryRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "headless-framework.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No repository root above {AppContext.BaseDirectory}.");
    }

    [GeneratedRegex(@"^(?<indent>\s*)```csharp\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex _OpenFence { get; }

    [GeneratedRegex(@"^\s*```\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex _CloseFence { get; }
}
