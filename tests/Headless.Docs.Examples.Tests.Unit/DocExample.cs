// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;

namespace Tests;

/// <summary>One fenced <c>csharp</c> block of a consumer guide.</summary>
/// <param name="Document">The guide's path relative to the repository root, as failures report it.</param>
/// <param name="Line">The 1-based line of the opening fence.</param>
/// <param name="Code">The block's content with the fence's indentation removed.</param>
/// <param name="Compiles">
/// <see langword="false"/> when the fence carries <see cref="DocExample.NoCompileMarker"/>: the block is deliberately
/// not a complete, valid program fragment.
/// </param>
public sealed partial record DocExample(string Document, int Line, string Code, bool Compiles)
{
    /// <summary>The info-string word that exempts a block from compilation: <c>```csharp no-compile</c>.</summary>
    public const string NoCompileMarker = "no-compile";

    /// <summary>The identifier the test cases use: <c>docs/llms/jobs.md:21</c>.</summary>
    public string Id => $"{Document}:{Line}";

    /// <summary>Reads every <c>csharp</c> block of the guide at <paramref name="path"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// A fence is unterminated or carries an unknown info-string word.
    /// </exception>
    public static IReadOnlyList<DocExample> Read(string path, string document)
    {
        var lines = File.ReadAllLines(path);
        var examples = new List<DocExample>();

        for (var index = 0; index < lines.Length; index++)
        {
            var open = _OpenFence.Match(lines[index]);

            if (!open.Success)
            {
                continue;
            }

            var indent = open.Groups["indent"].Value;
            var words = open.Groups["info"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var unknown = words.FirstOrDefault(word => !string.Equals(word, NoCompileMarker, StringComparison.Ordinal));

            if (unknown is not null)
            {
                throw new InvalidOperationException(
                    $"{document}:{index + 1}: unknown csharp fence word '{unknown}'; "
                        + $"only '{NoCompileMarker}' is recognized."
                );
            }

            var body = new List<string>();
            var close = index + 1;

            while (close < lines.Length && !_CloseFence.IsMatch(lines[close]))
            {
                var line = lines[close];
                body.Add(line.StartsWith(indent, StringComparison.Ordinal) ? line[indent.Length..] : line.TrimStart());
                close++;
            }

            if (close == lines.Length)
            {
                throw new InvalidOperationException($"{document}:{index + 1}: the csharp fence is never closed.");
            }

            examples.Add(new DocExample(document, index + 1, string.Join('\n', body), words.Length == 0));
            index = close;
        }

        return examples;
    }

    [GeneratedRegex(
        @"^(?<indent>\s*)```csharp(?:\s+(?<info>.*?))?\s*$",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex _OpenFence { get; }

    [GeneratedRegex(@"^\s*```\s*$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex _CloseFence { get; }
}
