// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using ILogger = Serilog.ILogger;

namespace Headless.Logging.Internal;

/// <summary>
/// Masks events written through Serilog's own <see cref="ILogger"/>, which never pass through the redacting logger
/// factory: the template's literal text, scalar property values, and the exception.
/// </summary>
/// <remarks>
/// <para>
/// Only the injected logger is wrapped. Events logged through <c>Microsoft.Extensions.Logging</c> reach Serilog through
/// the logger Serilog's factory holds, already masked by the redacting factory, so they are not scanned twice.
/// </para>
/// <para>
/// It masks the way the redacting logger does: scalar values in their invariant text form, the members of a
/// destructured value left to classification, and the exception through <see cref="LogTextMasker.Mask(Exception)"/>,
/// so it keeps its type and stack frames. A property an enricher adds after the event is written, such as one pushed
/// with <c>LogContext.PushProperty</c>, is not scanned.
/// </para>
/// </remarks>
internal sealed class MaskingSerilogLogger(ILogger inner, LogTextMasker masker) : ILogger
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
        new MaskingSerilogLogger(inner.ForContext(enricher), masker);

    public ILogger ForContext(IEnumerable<ILogEventEnricher> enrichers) =>
        new MaskingSerilogLogger(inner.ForContext(enrichers), masker);

    // A context property is attached by the inner logger after this one masks the event, so it is masked here, once.
    public ILogger ForContext(string propertyName, object? value, bool destructureObjects = false) =>
        new MaskingSerilogLogger(
            inner.ForContext(propertyName, destructureObjects ? value : masker.MaskValue(value), destructureObjects),
            masker
        );

    public ILogger ForContext<TSource>() => new MaskingSerilogLogger(inner.ForContext<TSource>(), masker);

    public ILogger ForContext(Type source) => new MaskingSerilogLogger(inner.ForContext(source), masker);

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
            var masked = _MaskValue(value);

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
