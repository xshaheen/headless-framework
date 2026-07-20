// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Headless.Exceptions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

/// <summary>Extensions for re-throwing, unwrapping, and rendering <see cref="Exception"/> instances.</summary>
[PublicAPI]
public static class HeadlessExceptionExtensions
{
    /// <summary>
    /// Uses <see cref="ExceptionDispatchInfo.Capture"/> to re-throw <paramref name="exception"/> while preserving its
    /// original stack trace. This method never returns; the declared return type only enables
    /// <c>throw exception.ReThrow()</c> usage so the compiler treats the call site as terminating.
    /// </summary>
    /// <param name="exception">Exception to be re-thrown.</param>
    /// <returns>This method never returns normally.</returns>
    /// <exception cref="Exception">Always re-throws <paramref name="exception"/> with its original stack trace preserved.</exception>
    [DoesNotReturn]
    public static Exception ReThrow(this Exception exception)
    {
        ExceptionDispatchInfo.Capture(exception).Throw();

        return exception;
    }

    /// <summary>Walks the <see cref="Exception.InnerException"/> chain and returns the deepest (root) exception.</summary>
    /// <param name="exception">The exception whose innermost cause is requested; may be <see langword="null"/>.</param>
    /// <returns>The innermost exception, or <see langword="null"/> when <paramref name="exception"/> is <see langword="null"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(exception))]
    public static Exception? GetInnermostException(this Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }

        // Walk the chain with Floyd's tortoise-and-hare so a cyclic InnerException chain (possible with custom
        // exceptions that override InnerException) cannot loop forever; on a detected cycle return the last seen node.
        var slow = exception;
        var fast = exception;

        while (fast.InnerException is { } next)
        {
            fast = next;

            if (fast.InnerException is not { } afterNext)
            {
                return fast;
            }

            fast = afterNext;
            slow = slow.InnerException!;

            if (ReferenceEquals(slow, fast))
            {
                return fast;
            }
        }

        return fast;
    }

    /// <summary>Wraps <paramref name="exception"/> in a <see cref="ConflictException"/> and throws it.</summary>
    /// <param name="exception">The exception to wrap; its message and instance become the conflict message and inner exception.</param>
    /// <exception cref="ConflictException">Always thrown, wrapping <paramref name="exception"/> as the inner exception.</exception>
    [DoesNotReturn]
    public static void ThrowConflictException(this Exception exception)
    {
        throw new ConflictException(exception.Message, exception);
    }

    /// <summary>
    /// Renders <paramref name="e"/> and its inner exceptions (flattening any <see cref="AggregateException"/>) into a
    /// human-readable multi-line string of type names, messages, and stack traces, bounded by <paramref name="maxDepth"/>.
    /// </summary>
    /// <param name="e">The exception to expand; may be <see langword="null"/>.</param>
    /// <param name="maxDepth">The maximum number of nested or aggregated exceptions to include.</param>
    /// <returns>The expanded text, or <see cref="string.Empty"/> when <paramref name="e"/> is <see langword="null"/>.</returns>
    [SystemPure]
    [JetBrainsPure]
    public static string ExpandMessage(this Exception? e, int maxDepth = 5)
    {
        if (e is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        if (e is AggregateException aggregate)
        {
            expandAggregate(sb, aggregate, maxDepth);
        }
        else
        {
            expandException(sb, e, maxDepth);
        }

        return sb.ToString();

        static void expandException(StringBuilder sb, Exception e, int max)
        {
            var depthLevel = 0;
            var exception = e;

            while (exception is not null && depthLevel++ < max)
            {
                if (depthLevel > 1)
                {
                    sb.Append(Environment.NewLine);
                }

                if (exception is AggregateException aggregateException)
                {
                    expandAggregate(sb, aggregateException, max);

                    break;
                }

                addException(sb, exception);

                exception = exception.InnerException;
            }
        }

        static void expandAggregate(StringBuilder sb, AggregateException e, int max)
        {
            e = e.Flatten();

            sb.Append("### AggregateException:");
            sb.Append(Environment.NewLine);

            var count = 0;

            foreach (var exception in e.InnerExceptions)
            {
                if (count >= max)
                {
                    break;
                }

                if (count > 0)
                {
                    sb.Append(Environment.NewLine);
                }

                addException(sb, exception.GetInnermostException());

                count++;
            }

            if (count >= max)
            {
                sb.Append(Environment.NewLine);
                sb.Append("... and ");
                sb.Append(e.InnerExceptions.Count - max);
                sb.Append(" more");
            }

            sb.Append(Environment.NewLine);
            sb.Append("###");
        }

        static void addException(StringBuilder sb, Exception e)
        {
            sb.Append(e.GetType().Name);
            sb.Append(": ");
            sb.Append(e.Message);

            var stackTrace = e.StackTrace;

            if (stackTrace is not null)
            {
                sb.Append(Environment.NewLine);
                sb.Append(stackTrace);
            }
        }
    }
}
