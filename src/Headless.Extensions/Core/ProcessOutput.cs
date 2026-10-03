// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace System.Diagnostics;

/// <summary>A single captured output line from a completed process run.</summary>
/// <param name="Type">Whether this line came from standard output or standard error.</param>
/// <param name="Text">The output line text.</param>
public sealed record ProcessOutput(ProcessOutputType Type, string Text)
{
    /// <summary>Returns the text, prefixed with <c>"error: "</c> for standard-error lines.</summary>
    /// <returns>The formatted representation of this line.</returns>
    public override string ToString()
    {
        return Type switch
        {
            ProcessOutputType.StandardError => "error: " + Text,
            _ => Text,
        };
    }
}

/// <summary>A read-only, ordered collection of <see cref="ProcessOutput"/> lines captured from a process run.</summary>
public sealed class ProcessOutputCollection : IReadOnlyList<ProcessOutput>
{
    private readonly IReadOnlyList<ProcessOutput> _output;

    internal ProcessOutputCollection(IReadOnlyList<ProcessOutput> output)
    {
        _output = output;
    }

    /// <summary>Gets the number of captured output lines.</summary>
    public int Count => _output.Count;

    /// <summary>Gets the captured output line at the specified <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based index of the line to retrieve.</param>
    /// <returns>The <see cref="ProcessOutput"/> at <paramref name="index"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="index"/> is outside the bounds of the collection.</exception>
    public ProcessOutput this[int index] => _output[index];

    /// <summary>Returns an enumerator over the captured output lines.</summary>
    /// <returns>An enumerator for the collection.</returns>
    [MustDisposeResource]
    public IEnumerator<ProcessOutput> GetEnumerator()
    {
        return _output.GetEnumerator();
    }

    [MustDisposeResource]
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>Concatenates every captured line, separated by blank lines, into a single string.</summary>
    /// <returns>All captured output lines joined together.</returns>
    public override string ToString()
    {
        var sb = new StringBuilder();

        foreach (var item in _output)
        {
            sb.Append(item).AppendLine().AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>Identifies which standard stream a captured <see cref="ProcessOutput"/> line came from.</summary>
/// <remarks>Additional members may be added in future versions; consumers switching on this enum should include a default case.</remarks>
[PublicAPI]
public enum ProcessOutputType
{
    /// <summary>A line written to standard output.</summary>
    StandardOutput = 0,

    /// <summary>A line written to standard error.</summary>
    StandardError = 1,
}
