// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using Headless.Checks;

namespace Headless.Logging;

/// <summary>
/// The stand-in log providers receive for an exception whose message an <see cref="ILogTextRedactor"/> matched, when a
/// masked copy of the exception's own type is not possible.
/// </summary>
/// <remarks>
/// <para>
/// The redacting logger first copies the exception, keeping its type and stack frames, and masks the copy's messages.
/// It falls back to this stand-in when the type builds <see cref="Exception.Message"/> from fields of its own, so the
/// copy would still carry the match, or when the runtime's exception fields are not where it expects them.
/// </para>
/// <para>
/// <see cref="ToString"/> returns the original's full text, masked: the original type name, message, every inner
/// exception, and the stack traces, in the original's own format. <see cref="Message"/> and <see cref="StackTrace"/>
/// return the original's, masked. <see cref="Exception.InnerException"/> and <see cref="InnerExceptions"/> hold the
/// original's inner exceptions, each replaced by its masked copy when its messages matched.
/// </para>
/// <para>
/// <see cref="ToString"/> and <see cref="StackTrace"/> are rendered and masked the first time a provider reads them,
/// then reused, so a provider that never prints the exception pays nothing for them.
/// </para>
/// <para>
/// A provider that reports <see cref="object.GetType"/> sees this type; <see cref="OriginalTypeName"/> names the
/// original. <see cref="Exception.TargetSite"/> is <see langword="null"/>, and <see cref="Data"/> holds the original's
/// entries unscanned.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class RedactedException : Exception
{
    private readonly Exception _original;
    private readonly Func<string, string> _redact;
    private readonly string _message;
    private string? _stackTrace;
    private string? _text;

    internal RedactedException(
        Exception original,
        string redactedMessage,
        IReadOnlyList<Exception> innerExceptions,
        Func<string, string> redact
    )
        : base(message: null, innerException: innerExceptions.Count > 0 ? innerExceptions[0] : null)
    {
        _original = Argument.IsNotNull(original);
        _redact = Argument.IsNotNull(redact);
        _message = Argument.IsNotNull(redactedMessage);

        OriginalTypeName = original.GetType().FullName ?? original.GetType().Name;
        InnerExceptions = innerExceptions;
        HResult = original.HResult;
        HelpLink = original.HelpLink;
        Source = original.Source;
    }

    /// <summary>The full name of the exception type this stands in for, for example <c>System.InvalidOperationException</c>.</summary>
    public string OriginalTypeName { get; }

    /// <summary>
    /// The original's inner exceptions: every member of an <see cref="AggregateException"/>, otherwise its one
    /// <see cref="Exception.InnerException"/>, or none.
    /// </summary>
    public IReadOnlyList<Exception> InnerExceptions { get; }

    /// <inheritdoc />
    public override string Message => _message;

    // A race renders the same text twice and keeps either copy, which is cheaper than a lock on every read.
    /// <inheritdoc />
    public override string? StackTrace => _stackTrace ??= _original.StackTrace is { } trace ? _redact(trace) : null;

    /// <inheritdoc />
    public override IDictionary Data => _original.Data;

    /// <inheritdoc />
    public override string ToString() => _text ??= _redact(_original.ToString());
}
