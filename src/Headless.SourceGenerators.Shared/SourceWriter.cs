// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace Headless.SourceGenerators;

/// <summary>
/// Writes indented C# source in its final layout, so generators emit formatted code directly instead of building an
/// unformatted string and paying for a parse-and-normalize pass on every run.
/// </summary>
/// <param name="newLine">
/// Line terminator. Defaults to CRLF, the terminator Roslyn's <c>NormalizeWhitespace</c> uses for code, so output stays
/// stable for generators that formatted with it before.
/// </param>
internal sealed class SourceWriter(string newLine = "\r\n")
{
    private const int _IndentSize = 4;
    private readonly StringBuilder _builder = new();
    private readonly string _newLine = newLine;
    private int _indentLevel;

    public void Indent() => _indentLevel++;

    public void Unindent() => _indentLevel--;

    /// <summary>Writes <paramref name="text"/> as one indented line.</summary>
    public void WriteLine(string text)
    {
        _builder.Append(' ', _indentLevel * _IndentSize).Append(text).Append(_newLine);
    }

    /// <summary>Writes an empty line with no indentation.</summary>
    public void WriteLine() => _builder.Append(_newLine);

    /// <summary>Writes <c>{</c> and indents the lines that follow.</summary>
    public void OpenBlock()
    {
        WriteLine("{");
        Indent();
    }

    /// <summary>Unindents and writes <c>}</c> followed by <paramref name="suffix"/>.</summary>
    public void CloseBlock(string suffix = "")
    {
        Unindent();
        WriteLine("}" + suffix);
    }

    /// <summary>Returns the written source without its final line terminator.</summary>
    public override string ToString()
    {
        var text = _builder.ToString();
        return text.EndsWith(_newLine, StringComparison.Ordinal)
            ? text.Substring(0, text.Length - _newLine.Length)
            : text;
    }

    public SourceText ToSourceText() => SourceText.From(ToString(), Encoding.UTF8);
}
