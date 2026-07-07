// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Logging;

// ReSharper disable once TemplateIsNotCompileTimeConstantProblem
#pragma warning disable CA2254 // Template should be a string literal.
/// <summary>
/// Extension methods on <see cref="ILogger" /> and <see cref="LogState" /> that add structured-logging
/// helpers: scoped property bags, level-gated log methods with inline scope building, and convenience
/// tag/critical markers.
/// </summary>
[PublicAPI]
public static class HeadlessLoggerExtensions
{
    extension(ILogger logger)
    {
        /// <summary>
        /// Begins a structured log scope by applying <paramref name="stateBuilder" /> to a new
        /// <see cref="LogState" /> and pushing the resulting property bag onto the logger's ambient scope.
        /// </summary>
        /// <param name="stateBuilder">
        /// A delegate that populates a fresh <see cref="LogState" /> with the desired properties.
        /// Invoked immediately; the returned <see cref="LogState" /> becomes the scope payload.
        /// </param>
        /// <returns>
        /// An <see cref="IDisposable" /> scope handle, or <see langword="null" /> if the logger does not
        /// support scopes. Dispose the handle to pop the scope.
        /// </returns>
        public IDisposable? BeginScope(Func<LogState, LogState> stateBuilder)
        {
            var logState = stateBuilder(new LogState());

            return logger.BeginScope(logState);
        }

        /// <summary>
        /// Begins a structured log scope containing a single key/value property.
        /// </summary>
        /// <param name="property">The property name to attach to the scope.</param>
        /// <param name="value">The property value. May be <see langword="null" />.</param>
        /// <returns>
        /// An <see cref="IDisposable" /> scope handle, or <see langword="null" /> if the logger does not
        /// support scopes. Dispose the handle to pop the scope.
        /// </returns>
        public IDisposable? BeginScope(string property, object? value)
        {
            return logger.BeginScope(b => b.Property(property, value));
        }

        /// <summary>
        /// Logs a <see cref="LogLevel.Debug" /> message with an inline structured scope.
        /// The scope is opened immediately before the log call and disposed on return.
        /// Does nothing if <see cref="LogLevel.Debug" /> is not enabled.
        /// </summary>
        /// <param name="stateBuilder">
        /// A delegate that populates the scope's <see cref="LogState" /> with contextual properties.
        /// </param>
        /// <param name="message">The structured message template.</param>
        /// <param name="args">Arguments to substitute into <paramref name="message" />.</param>
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

        /// <summary>
        /// Logs a <see cref="LogLevel.Trace" /> message with an inline structured scope.
        /// The scope is opened immediately before the log call and disposed on return.
        /// Does nothing if <see cref="LogLevel.Trace" /> is not enabled.
        /// </summary>
        /// <param name="stateBuilder">
        /// A delegate that populates the scope's <see cref="LogState" /> with contextual properties.
        /// </param>
        /// <param name="message">The structured message template.</param>
        /// <param name="args">Arguments to substitute into <paramref name="message" />.</param>
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

        /// <summary>
        /// Logs an <see cref="LogLevel.Information" /> message with an inline structured scope.
        /// The scope is opened immediately before the log call and disposed on return.
        /// Does nothing if <see cref="LogLevel.Information" /> is not enabled.
        /// </summary>
        /// <param name="stateBuilder">
        /// A delegate that populates the scope's <see cref="LogState" /> with contextual properties.
        /// </param>
        /// <param name="message">The structured message template.</param>
        /// <param name="args">Arguments to substitute into <paramref name="message" />.</param>
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

        /// <summary>
        /// Logs a <see cref="LogLevel.Warning" /> message with an inline structured scope.
        /// The scope is opened immediately before the log call and disposed on return.
        /// Does nothing if <see cref="LogLevel.Warning" /> is not enabled.
        /// </summary>
        /// <param name="stateBuilder">
        /// A delegate that populates the scope's <see cref="LogState" /> with contextual properties.
        /// </param>
        /// <param name="message">The structured message template.</param>
        /// <param name="args">Arguments to substitute into <paramref name="message" />.</param>
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

        /// <summary>
        /// Logs an <see cref="LogLevel.Error" /> message with an inline structured scope.
        /// The scope is opened immediately before the log call and disposed on return.
        /// Does nothing if <see cref="LogLevel.Error" /> is not enabled.
        /// </summary>
        /// <param name="stateBuilder">
        /// A delegate that populates the scope's <see cref="LogState" /> with contextual properties.
        /// </param>
        /// <param name="message">The structured message template.</param>
        /// <param name="args">Arguments to substitute into <paramref name="message" />.</param>
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

        /// <summary>
        /// Logs an <see cref="LogLevel.Error" /> message with an associated exception and an inline
        /// structured scope. The scope is opened immediately before the log call and disposed on return.
        /// Does nothing if <see cref="LogLevel.Error" /> is not enabled.
        /// </summary>
        /// <param name="stateBuilder">
        /// A delegate that populates the scope's <see cref="LogState" /> with contextual properties.
        /// </param>
        /// <param name="exception">The exception to attach to the log entry.</param>
        /// <param name="message">The structured message template.</param>
        /// <param name="args">Arguments to substitute into <paramref name="message" />.</param>
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

        /// <summary>
        /// Logs a <see cref="LogLevel.Critical" /> message with an inline structured scope.
        /// The scope is opened immediately before the log call and disposed on return.
        /// Does nothing if <see cref="LogLevel.Critical" /> is not enabled.
        /// </summary>
        /// <param name="stateBuilder">
        /// A delegate that populates the scope's <see cref="LogState" /> with contextual properties.
        /// </param>
        /// <param name="message">The structured message template.</param>
        /// <param name="args">Arguments to substitute into <paramref name="message" />.</param>
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

    /// <summary>
    /// Conditionally appends a <c>"Critical"</c> tag to the log state's tag list.
    /// Useful for flagging a log entry as operationally critical without changing its
    /// <see cref="LogLevel" />.
    /// </summary>
    /// <param name="builder">The <see cref="LogState" /> to modify.</param>
    /// <param name="isCritical">
    /// When <see langword="true" /> (the default), the <c>"Critical"</c> tag is added.
    /// When <see langword="false" />, <paramref name="builder" /> is returned unchanged.
    /// </param>
    /// <returns>The same <paramref name="builder" /> instance, for chaining.</returns>
    public static LogState Critical(this LogState builder, bool isCritical = true)
    {
        return isCritical ? builder.Tag("Critical") : builder;
    }

    /// <summary>
    /// Appends a single tag to the log state's <c>"Tags"</c> property list.
    /// Duplicate tags (case-insensitive) are silently ignored.
    /// </summary>
    /// <param name="builder">The <see cref="LogState" /> to modify.</param>
    /// <param name="tag">The tag string to append.</param>
    /// <returns>The same <paramref name="builder" /> instance, for chaining.</returns>
    public static LogState Tag(this LogState builder, string tag)
    {
        return builder.Tag([tag]);
    }

    /// <summary>
    /// Appends one or more tags to the log state's <c>"Tags"</c> property list.
    /// Duplicate tags (case-insensitive) are silently ignored.
    /// </summary>
    /// <param name="builder">The <see cref="LogState" /> to modify.</param>
    /// <param name="tags">The tags to append. May be empty.</param>
    /// <returns>The same <paramref name="builder" /> instance, for chaining.</returns>
    public static LogState Tag(this LogState builder, params ReadOnlySpan<string> tags)
    {
        // Reuse the existing "Tags" list when present; only allocate a new one when this is the first tag,
        // rather than allocating unconditionally and discarding it on the reuse path.
        var tagList = builder.ContainsProperty("Tags") && builder["Tags"] is List<string> list ? list : [];

        foreach (var tag in tags)
        {
            if (!tagList.Exists(s => s.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            {
                tagList.Add(tag);
            }
        }

        return builder.Property("Tags", tagList);
    }

    /// <summary>
    /// Adds all non-null-keyed pairs from <paramref name="collection" /> as individual properties on
    /// the log state. Pairs with a <see langword="null" /> key are skipped. If
    /// <paramref name="collection" /> itself is <see langword="null" />, the builder is returned
    /// unchanged.
    /// </summary>
    /// <param name="builder">The <see cref="LogState" /> to modify.</param>
    /// <param name="collection">
    /// The key/value pairs to add. May be <see langword="null" />, in which case this method is a no-op.
    /// </param>
    /// <returns>The same <paramref name="builder" /> instance, for chaining.</returns>
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
