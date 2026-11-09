// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Checks;

namespace Headless.Logging.Internal;

/// <summary>An <see cref="ILogTextRedactor"/> that replaces every match of a pattern.</summary>
/// <remarks>
/// Give the pattern a match timeout, because it runs on every log entry: a pattern that times out fails closed, and the
/// whole text is replaced, so a slow match can never let the value through.
/// </remarks>
/// <param name="pattern">The shape to mask, for example <c>new Regex(@"\b\d{14}\b", RegexOptions.None, TimeSpan.FromMilliseconds(50))</c>.</param>
/// <param name="replacement">The text that replaces each match. Defaults to <c>***</c>.</param>
internal sealed class RegexLogTextRedactor(Regex pattern, string replacement = "***") : ILogTextRedactor
{
    private readonly Regex _pattern = Argument.IsNotNull(pattern);
    private readonly string _replacement = Argument.IsNotNull(replacement);

    /// <inheritdoc />
    public string Redact(string text)
    {
        Argument.IsNotNull(text);

        try
        {
            return _pattern.IsMatch(text) ? _pattern.Replace(text, _replacement) : text;
        }
        catch (RegexMatchTimeoutException)
        {
            return _replacement;
        }
    }
}
