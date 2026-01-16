// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using Framework.Exceptions;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

[PublicAPI]
public static class ExceptionExtensions
{
    /// <summary>Uses <see cref="Capture"/> method to re-throws exception while preserving stack trace.</summary>
    /// <param name="exception">Exception to be re-thrown</param>
    [DoesNotReturn]
    public static Exception ReThrow(this Exception exception)
    {
        ExceptionDispatchInfo.Capture(exception).Throw();

        return exception;
    }

    [SystemPure]
    [JetBrainsPure]
    [return: NotNullIfNotNull(nameof(exception))]
    public static Exception? GetInnermostException(this Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }

        var current = exception;

        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current;
    }

    [DoesNotReturn]
    public static void ThrowConflictException(this Exception exception)
    {
        throw new ConflictException(exception.Message, exception);
    }

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
