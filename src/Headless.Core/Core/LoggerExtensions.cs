// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Logging;

// ReSharper disable once TemplateIsNotCompileTimeConstantProblem
#pragma warning disable CA2254 // Template should be a string literal.
[PublicAPI]
public static class HeadlessLoggerExtensions
{
    extension(ILogger logger)
    {
        public IDisposable? BeginScope(Func<LogState, LogState> stateBuilder)
        {
            var logState = stateBuilder(new LogState());

            return logger.BeginScope(logState);
        }

        public IDisposable? BeginScope(string property, object? value)
        {
            return logger.BeginScope(b => b.Property(property, value));
        }

        public void LogDebug(
            Func<LogState, LogState> stateBuilder,
            [StructuredMessageTemplate] string message,
            params object?[] args
        )
        {
            if (!logger.IsEnabled(LogLevel.Debug))
            {
                return;
            }

            using (BeginScope(logger, stateBuilder))
            {
                logger.LogDebug(message, args);
            }
        }

        public void LogTrace(
            Func<LogState, LogState> stateBuilder,
            [StructuredMessageTemplate] string message,
            params object?[] args
        )
        {
            if (!logger.IsEnabled(LogLevel.Trace))
            {
                return;
            }

            using (BeginScope(logger, stateBuilder))
            {
                logger.LogTrace(message, args);
            }
        }

        public void LogInformation(
            Func<LogState, LogState> stateBuilder,
            [StructuredMessageTemplate] string message,
            params object?[] args
        )
        {
            if (!logger.IsEnabled(LogLevel.Information))
            {
                return;
            }

            using (BeginScope(logger, stateBuilder))
            {
                logger.LogInformation(message, args);
            }
        }

        public void LogWarning(
            Func<LogState, LogState> stateBuilder,
            [StructuredMessageTemplate] string message,
            params object?[] args
        )
        {
            if (!logger.IsEnabled(LogLevel.Warning))
            {
                return;
            }

            using (BeginScope(logger, stateBuilder))
            {
                logger.LogWarning(message, args);
            }
        }

        public void LogError(
            Func<LogState, LogState> stateBuilder,
            [StructuredMessageTemplate] string message,
            params object?[] args
        )
        {
            if (!logger.IsEnabled(LogLevel.Error))
            {
                return;
            }

            using (BeginScope(logger, stateBuilder))
            {
                logger.LogError(message, args);
            }
        }

        public void LogError(
            Func<LogState, LogState> stateBuilder,
            Exception exception,
            [StructuredMessageTemplate] string message,
            params object?[] args
        )
        {
            if (!logger.IsEnabled(LogLevel.Error))
            {
                return;
            }

            using (BeginScope(logger, stateBuilder))
            {
                logger.LogError(exception, message, args);
            }
        }

        public void LogCritical(
            Func<LogState, LogState> stateBuilder,
            [StructuredMessageTemplate] string message,
            params object?[] args
        )
        {
            if (!logger.IsEnabled(LogLevel.Critical))
            {
                return;
            }

            using (BeginScope(logger, stateBuilder))
            {
                logger.LogCritical(message, args);
            }
        }
    }

    public static LogState Critical(this LogState builder, bool isCritical = true)
    {
        return isCritical ? builder.Tag("Critical") : builder;
    }

    public static LogState Tag(this LogState builder, string tag)
    {
        return builder.Tag([tag]);
    }

    public static LogState Tag(this LogState builder, params ReadOnlySpan<string> tags)
    {
        var tagList = new List<string>();

        if (builder.ContainsProperty("Tags") && builder["Tags"] is List<string> list)
        {
            tagList = list;
        }

        foreach (var tag in tags)
        {
            if (!tagList.Exists(s => s.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            {
                tagList.Add(tag);
            }
        }

        return builder.Property("Tags", tagList);
    }

    public static LogState Properties(this LogState builder, ICollection<KeyValuePair<string?, string?>>? collection)
    {
        if (collection is null)
        {
            return builder;
        }

        foreach (var pair in collection)
        {
            if (pair.Key is not null)
            {
                builder.Property(pair.Key, pair.Value);
            }
        }

        return builder;
    }
}

public sealed class LogState : IEnumerable<KeyValuePair<string, object?>>
{
    private readonly Dictionary<string, object?> _state = new(StringComparer.Ordinal);

    public int Count => _state.Count;

    public object? this[string property]
    {
        get { return _state[property]; }
        set { _state[property] = value; }
    }

    public LogState Property(string property, object? value)
    {
        Argument.IsNotNull(property);
        // Indexer (last-write-wins) rather than Dictionary.Add: Tag()/Critical()/Properties()
        // re-write existing keys, and structured-logging state must never throw on a duplicate.
        _state[property] = value;

        return this;
    }

    public LogState PropertyIf(string property, object? value, bool condition)
    {
        Argument.IsNotNull(property);

        if (condition)
        {
            _state[property] = value;
        }

        return this;
    }

    public bool ContainsProperty(string property)
    {
        return _state.ContainsKey(property);
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _state.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
