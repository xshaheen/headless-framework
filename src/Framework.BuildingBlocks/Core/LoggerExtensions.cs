// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Logging;

// ReSharper disable once TemplateIsNotCompileTimeConstantProblem
#pragma warning disable CA2254 // Template should be a string literal.
[PublicAPI]
public static class LoggerExtensions
{
    public static IDisposable? BeginScope(this ILogger logger, Func<LogState, LogState> stateBuilder)
    {
        var logState = stateBuilder(new LogState());

        return logger.BeginScope(logState);
    }

    public static IDisposable? BeginScope(this ILogger logger, string property, object? value)
    {
        return logger.BeginScope(b => b.Property(property, value));
    }

    public static void LogDebug(
        this ILogger logger,
        Func<LogState, LogState> stateBuilder,
        [StructuredMessageTemplate] string message,
        params object?[] args
    )
    {
        using (BeginScope(logger, stateBuilder))
        {
            logger.LogDebug(message, args);
        }
    }

    public static void LogTrace(
        this ILogger logger,
        Func<LogState, LogState> stateBuilder,
        [StructuredMessageTemplate] string message,
        params object[] args
    )
    {
        using (BeginScope(logger, stateBuilder))
        {
            logger.LogTrace(message, args);
        }
    }

    public static void LogInformation(
        this ILogger logger,
        Func<LogState, LogState> stateBuilder,
        [StructuredMessageTemplate] string message,
        params object[] args
    )
    {
        using (BeginScope(logger, stateBuilder))
        {
            logger.LogInformation(message, args);
        }
    }

    public static void LogWarning(
        this ILogger logger,
        Func<LogState, LogState> stateBuilder,
        [StructuredMessageTemplate] string message,
        params object[] args
    )
    {
        using (BeginScope(logger, stateBuilder))
        {
            logger.LogWarning(message, args);
        }
    }

    public static void LogError(
        this ILogger logger,
        Func<LogState, LogState> stateBuilder,
        [StructuredMessageTemplate] string message,
        params object[] args
    )
    {
        using (BeginScope(logger, stateBuilder))
        {
            logger.LogError(message, args);
        }
    }

    public static void LogError(
        this ILogger logger,
        Func<LogState, LogState> stateBuilder,
        Exception exception,
        [StructuredMessageTemplate] string message,
        params object[] args
    )
    {
        using (BeginScope(logger, stateBuilder))
        {
            logger.LogError(exception, message, args);
        }
    }

    public static void LogCritical(
        this ILogger logger,
        Func<LogState, LogState> stateBuilder,
        [StructuredMessageTemplate] string message,
        params object[] args
    )
    {
        using (BeginScope(logger, stateBuilder))
        {
            logger.LogCritical(message, args);
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
        _state.Add(property, value);

        return this;
    }

    public LogState PropertyIf(string property, object? value, bool condition)
    {
        if (condition)
        {
            _state.Add(property, value);
        }

        return this;
    }

    public bool ContainsProperty(string property)
    {
        return _state.ContainsKey(property);
    }

    [MustDisposeResource]
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _state.GetEnumerator();

    [MustDisposeResource]
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
