// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using Framework.Primitives;

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

    [DoesNotReturn]
    public static void ThrowConflictException(this Exception ex)
    {
        throw new ConflictException(ex.ExpandExceptionMessage(), ex);
    }

    [SystemPure]
    [JetBrainsPure]
    public static string ExpandExceptionMessage(this Exception ex)
    {
        const int maxDepthLevel = 5;

        var builder = new StringBuilder();
        var separator = Environment.NewLine;
        var exception = ex;
        var depthLevel = 0;

        while (exception is not null && depthLevel++ < maxDepthLevel)
        {
            if (builder.Length > 0)
            {
                builder.Append(separator);
            }

            builder.Append(ex.Message);
            builder.Append(separator);
            builder.Append(ex.StackTrace);

            exception = exception.InnerException;
        }

        return builder.ToString();
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
}
