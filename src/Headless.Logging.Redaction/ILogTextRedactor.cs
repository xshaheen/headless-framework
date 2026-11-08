// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Logging;

/// <summary>
/// Masks value shapes an app knows are sensitive, such as a national id or a card number, in the text of every log
/// entry: the rendered message, property values, and scope values.
/// </summary>
/// <remarks>
/// <para>
/// This is a net for text no classification reaches: a plain <c>{Value}</c> hole, an interpolated message, a value
/// that arrives as part of a larger string. Classify what you can first; a text redactor runs on every entry of every
/// category, so it costs a scan per string, and a shape-based match can mask an unrelated value that happens to look
/// the same.
/// </para>
/// <para>
/// Register implementations as <see cref="ILogTextRedactor"/> singletons (or with <c>AddLogTextRedaction</c>) and call
/// <c>AddHeadlessLogRedaction()</c>. Nothing is scanned until one is registered. Exception messages and stack traces are
/// not rewritten: providers read them from the exception object. A non-string value is scanned in its invariant text
/// form and replaced by its masked text only when that text matches; the members of a destructured <c>{@Value}</c> are
/// not scanned.
/// </para>
/// </remarks>
[PublicAPI]
public interface ILogTextRedactor
{
    /// <summary>Returns <paramref name="text"/> with every sensitive shape masked, or the same instance when none matched.</summary>
    /// <param name="text">The text to scan.</param>
    /// <returns>The masked text.</returns>
    string Redact(string text);
}
