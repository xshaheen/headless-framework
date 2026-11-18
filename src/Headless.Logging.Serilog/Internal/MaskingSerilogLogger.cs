// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Compliance.Redaction;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using ILogger = Serilog.ILogger;

namespace Headless.Logging.Internal;

/// <summary>
/// Masks events written through Serilog's own <see cref="ILogger"/>, which never pass through the redacting logger
/// factory: the template's literal text, property values, and the exception.
/// </summary>
/// <remarks>
/// <para>
/// Only the injected logger is wrapped. Events logged through <c>Microsoft.Extensions.Logging</c> reach Serilog through
/// the logger Serilog's factory holds, already masked by the redacting factory, so they are not scanned twice.
/// </para>
/// <para>
/// It masks the way the redacting logger does. A property whose name the app classified (<c>ClassifyProperty</c>) goes
/// through the same redactor the logger factory uses, so both paths yield the same pseudonym; a sequence is redacted
/// element by element, and a destructured value under that name is redacted as a whole, from its rendered text. Every
/// other scalar value is text-masked in its invariant text form, the members of a destructured value are left to
/// classification, and the exception goes through <see cref="LogTextMasker.Mask(Exception)"/>, so it keeps its type
/// and stack frames. A property an enricher adds after the event is written, such as one pushed with
/// <c>LogContext.PushProperty</c>, is not scanned.
/// </para>
/// </remarks>
internal sealed class MaskingSerilogLogger(
    ILogger inner,
    LogTextMasker masker,
    FrozenDictionary<string, Redactor> propertyRedactors
) : ILogger
{
    public bool IsEnabled(LogEventLevel level) => inner.IsEnabled(level);

    public bool BindMessageTemplate(
        string messageTemplate,
        object?[]? propertyValues,
        [NotNullWhen(true)] out MessageTemplate? parsedTemplate,
        [NotNullWhen(true)] out IEnumerable<LogEventProperty>? boundProperties
    ) => inner.BindMessageTemplate(messageTemplate, propertyValues, out parsedTemplate, out boundProperties);

    public bool BindProperty(
        string? propertyName,
        object? value,
        bool destructureObjects,
        [NotNullWhen(true)] out LogEventProperty? property
    ) => inner.BindProperty(propertyName, value, destructureObjects, out property);

    public ILogger ForContext(ILogEventEnricher enricher) =>
        new MaskingSerilogLogger(inner.ForContext(enricher), masker, propertyRedactors);

    public ILogger ForContext(IEnumerable<ILogEventEnricher> enrichers) =>
        new MaskingSerilogLogger(inner.ForContext(enrichers), masker, propertyRedactors);

    // A context property is attached by the inner logger after this one masks the event, so it is masked here, once.
    public ILogger ForContext(string propertyName, object? value, bool destructureObjects = false)
    {
        ILogger context;

        if (value is not null && propertyRedactors.TryGetValue(propertyName, out var redactor))
        {
            // Redacted before Serilog captures it, so a destructured value is never bound with its members in clear.
            object redacted = value is IEnumerable items and not string
                ? items.Cast<object?>().Select(item => item is null ? null : redactor.Redact(item)).ToArray()
                : redactor.Redact(value);

            context = inner.ForContext(propertyName, redacted, destructureObjects: false);
        }
        else
        {
            context = inner.ForContext(
                propertyName,
                destructureObjects ? value : masker.MaskValue(value),
                destructureObjects
            );
        }

        return new MaskingSerilogLogger(context, masker, propertyRedactors);
    }

    public ILogger ForContext<TSource>() =>
        new MaskingSerilogLogger(inner.ForContext<TSource>(), masker, propertyRedactors);

    public ILogger ForContext(Type source) =>
        new MaskingSerilogLogger(inner.ForContext(source), masker, propertyRedactors);

    public void Write(LogEvent logEvent)
    {
        inner.Write(_Mask(logEvent));
    }

    // Returns the same event, with no copy, when nothing in it matched.
    private LogEvent _Mask(LogEvent logEvent)
    {
        var exception = logEvent.Exception is { } raw ? masker.Mask(raw) : null;
        var template = _MaskTemplate(logEvent.MessageTemplate);
        List<LogEventProperty>? properties = null;
        var index = 0;

        foreach (var (name, value) in logEvent.Properties)
        {
            var masked = propertyRedactors.TryGetValue(name, out var redactor)
                ? _Redact(value, redactor)
                : _MaskValue(value);

            if (properties is null && !ReferenceEquals(masked, value))
            {
                // Copied only once a value changed, so an event with no match costs no list.
                properties = new List<LogEventProperty>(logEvent.Properties.Count);
                properties.AddRange(
                    logEvent.Properties.Take(index).Select(static p => new LogEventProperty(p.Key, p.Value))
                );
            }

            properties?.Add(new LogEventProperty(name, masked));
            index++;
        }

        if (
            ReferenceEquals(exception, logEvent.Exception)
            && ReferenceEquals(template, logEvent.MessageTemplate)
            && properties is null
        )
        {
            return logEvent;
        }

        return new LogEvent(
            logEvent.Timestamp,
            logEvent.Level,
            exception,
            template,
            properties ?? [.. logEvent.Properties.Select(static p => new LogEventProperty(p.Key, p.Value))],
            logEvent.TraceId ?? default,
            logEvent.SpanId ?? default
        );
    }

    // The value goes through the redactor the logger factory uses for the same name. A sequence keeps its shape, so a
    // sink sees one redacted value per element, as the factory's path hands one per element to providers.
    private static LogEventPropertyValue _Redact(LogEventPropertyValue value, Redactor redactor)
    {
        return value switch
        {
            ScalarValue { Value: null } => value,
            ScalarValue { Value: { } scalar } => new ScalarValue(redactor.Redact(scalar)),
            SequenceValue sequence => new SequenceValue(
                sequence.Elements.Select(element => _Redact(element, redactor))
            ),
            // A structure or dictionary under a classified name is one value: its rendered text is redacted whole, so
            // none of its members reaches a sink.
            _ => new ScalarValue(redactor.Redact(value.ToString(format: null, CultureInfo.InvariantCulture))),
        };
    }

    private LogEventPropertyValue _MaskValue(LogEventPropertyValue value)
    {
        if (value is not ScalarValue { Value: { } scalar })
        {
            return value;
        }

        var masked = masker.MaskValue(scalar);

        return ReferenceEquals(masked, scalar) ? value : new ScalarValue(masked);
    }

    // An interpolated message reaches Serilog as the template itself, so its literal text is scanned; property tokens
    // render from the masked values.
    private MessageTemplate _MaskTemplate(MessageTemplate template)
    {
        var text = template.Text;

        if (ReferenceEquals(masker.Mask(text), text))
        {
            return template;
        }

        MessageTemplateToken[] tokens =
        [
            .. template.Tokens.Select(token =>
                token is TextToken literal
                && masker.Mask(literal.Text) is var masked
                && !ReferenceEquals(masked, literal.Text)
                    ? new TextToken(masked)
                    : token
            ),
        ];

        return new MessageTemplate(string.Concat(tokens.Select(static token => token.ToString())), tokens);
    }
}
