// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="ILoggingBuilder"/> extension methods.
/// </summary>
public static class LoggingBuilderExtensions
{
    /// <summary>
    /// Executes the specified action if the specified <paramref name="condition"/> is <see langword="true"/> which can be
    /// used to conditionally add to the logging builder.
    /// </summary>
    /// <param name="loggingBuilder">The logging builder.</param>
    /// <param name="condition">If set to <see langword="true"/> the action is executed.</param>
    /// <param name="action">The action used to add to the logging builder.</param>
    /// <returns>The same logging builder.</returns>
    public static ILoggingBuilder AddIf(
        this ILoggingBuilder loggingBuilder,
        bool condition,
        Func<ILoggingBuilder, ILoggingBuilder> action
    )
    {
        Argument.IsNotNull(loggingBuilder);
        Argument.IsNotNull(action);

        if (condition)
        {
            loggingBuilder = action(loggingBuilder);
        }

        return loggingBuilder;
    }

    /// <summary>
    /// Executes the specified <paramref name="ifAction"/> if the specified <paramref name="condition"/> is
    /// <see langword="true"/>, otherwise executes the <paramref name="elseAction"/>. This can be used to conditionally add to
    /// the logging builder.
    /// </summary>
    /// <param name="loggingBuilder">The logging builder.</param>
    /// <param name="condition">If set to <see langword="true"/> the <paramref name="ifAction"/> is executed, otherwise the
    /// <paramref name="elseAction"/> is executed.</param>
    /// <param name="ifAction">The action used to add to the logging builder if the condition is <see langword="true"/>.</param>
    /// <param name="elseAction">The action used to add to the logging builder if the condition is <see langword="false"/>.</param>
    /// <returns>The same logging builder.</returns>
    public static ILoggingBuilder AddIfElse(
        this ILoggingBuilder loggingBuilder,
        bool condition,
        Func<ILoggingBuilder, ILoggingBuilder> ifAction,
        Func<ILoggingBuilder, ILoggingBuilder> elseAction
    )
    {
        Argument.IsNotNull(loggingBuilder);
        Argument.IsNotNull(ifAction);
        Argument.IsNotNull(elseAction);

        return condition ? ifAction(loggingBuilder) : elseAction(loggingBuilder);
    }
}
