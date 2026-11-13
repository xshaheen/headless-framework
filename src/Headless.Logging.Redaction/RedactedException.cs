// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using Headless.Checks;

namespace Headless.Logging;

/// <summary>
/// The stand-in log providers receive instead of an exception whose text an <see cref="ILogTextRedactor"/> masked.
/// </summary>
/// <remarks>
/// <para>
/// An exception cannot be changed in place without changing it for the code that caught it, so the redacting logger
/// hands providers this copy instead. <see cref="ToString"/> returns the original's full text, masked: the original
/// type name, message, every inner exception, and the stack traces, in the original's own format. <see cref="Message"/>
/// and <see cref="StackTrace"/> return the original's, masked. <see cref="Exception.InnerException"/> and
/// <see cref="InnerExceptions"/> hold the original's inner exceptions, each replaced by its own stand-in when its text
/// matched.
/// </para>
/// <para>
/// A provider that reports <see cref="object.GetType"/> sees this type; <see cref="OriginalTypeName"/> names the
/// original. <see cref="Exception.TargetSite"/> is <see langword="null"/>, and <see cref="Data"/> holds the original's
/// entries unscanned. Only an exception whose text matched is replaced; any other reaches providers as the same object.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class RedactedException : Exception
{
    private readonly string _message;
    private readonly string? _stackTrace;
    private readonly string _text;
    private readonly IDictionary _data;

    internal RedactedException(
        Exception original,
        Func<string, string> redact,
        string redactedText,
        IReadOnlyList<Exception> innerExceptions
    )
        : base(null, innerExceptions.Count > 0 ? innerExceptions[0] : null)
    {
        Argument.IsNotNull(original);

        OriginalTypeName = original.GetType().FullName ?? original.GetType().Name;
        InnerExceptions = innerExceptions;
        _message = redact(original.Message);
        _stackTrace = original.StackTrace is { } stackTrace ? redact(stackTrace) : null;
        _text = redactedText;
        _data = original.Data;
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

    /// <inheritdoc />
    public override string? StackTrace => _stackTrace;

    /// <inheritdoc />
    public override IDictionary Data => _data;

    /// <inheritdoc />
    public override string ToString() => _text;
}
