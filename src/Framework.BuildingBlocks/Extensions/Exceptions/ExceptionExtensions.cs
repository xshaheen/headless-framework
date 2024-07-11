using System.Diagnostics.CodeAnalysis;
using System.Text;
using Framework.BuildingBlocks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

public static class ExceptionExtensions
{
    [DoesNotReturn]
    public static void ThrowConflictException(this Exception ex)
    {
        throw new ConflictException(ex.ExpandExceptionMessage(), ex);
    }

    [SystemPure, JetBrainsPure]
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
}
