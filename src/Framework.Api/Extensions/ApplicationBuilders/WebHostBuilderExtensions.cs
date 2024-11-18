// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Microsoft.AspNetCore.Hosting;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// <see cref="IWebHostBuilder"/> extension methods.
/// </summary>
[PublicAPI]
public static class WebHostBuilderExtensions
{
    /// <summary>
    /// Executes the specified action if the specified <paramref name="condition"/> is <see langword="true"/> which can be
    /// used to conditionally add to the web host builder.
    /// </summary>
    /// <param name="webHostBuilder">The web host builder.</param>
    /// <param name="condition">If set to <see langword="true"/> the action is executed.</param>
    /// <param name="action">The action used to add to the web host builder.</param>
    /// <returns>The same web host builder.</returns>
    public static IWebHostBuilder UseIf(
        this IWebHostBuilder webHostBuilder,
        bool condition,
        Func<IWebHostBuilder, IWebHostBuilder> action
    )
    {
        Argument.IsNotNull(webHostBuilder);
        Argument.IsNotNull(action);

        if (condition)
        {
            webHostBuilder = action(webHostBuilder);
        }

        return webHostBuilder;
    }

    /// <summary>
    /// Executes the specified action if the specified <paramref name="condition"/> is <see langword="true"/> which can be
    /// used to conditionally add to the web host builder.
    /// </summary>
    /// <param name="webHostBuilder">The web host builder.</param>
    /// <param name="condition">If <see langword="true"/> is returned the action is executed.</param>
    /// <param name="action">The action used to add to the web host builder.</param>
    /// <returns>The same web host builder.</returns>
    public static IWebHostBuilder UseIf(
        this IWebHostBuilder webHostBuilder,
        Func<IWebHostBuilder, bool> condition,
        Func<IWebHostBuilder, IWebHostBuilder> action
    )
    {
        Argument.IsNotNull(webHostBuilder);
        Argument.IsNotNull(condition);
        Argument.IsNotNull(action);

        if (condition(webHostBuilder))
        {
            webHostBuilder = action(webHostBuilder);
        }

        return webHostBuilder;
    }

    /// <summary>
    /// Executes the specified <paramref name="ifAction"/> if the specified <paramref name="condition"/> is
    /// <see langword="true"/>, otherwise executes the <paramref name="elseAction"/>. This can be used to conditionally add to
    /// the web host builder.
    /// </summary>
    /// <param name="webHostBuilder">The web host builder.</param>
    /// <param name="condition">If set to <see langword="true"/> the <paramref name="ifAction"/> is executed, otherwise the
    /// <paramref name="elseAction"/> is executed.</param>
    /// <param name="ifAction">The action used to add to the web host builder if the condition is <see langword="true"/>.</param>
    /// <param name="elseAction">The action used to add to the web host builder if the condition is <see langword="false"/>.</param>
    /// <returns>The same web host builder.</returns>
    public static IWebHostBuilder UseIfElse(
        this IWebHostBuilder webHostBuilder,
        bool condition,
        Func<IWebHostBuilder, IWebHostBuilder> ifAction,
        Func<IWebHostBuilder, IWebHostBuilder> elseAction
    )
    {
        Argument.IsNotNull(webHostBuilder);
        Argument.IsNotNull(ifAction);
        Argument.IsNotNull(elseAction);

        return condition ? ifAction(webHostBuilder) : elseAction(webHostBuilder);
    }

    /// <summary>
    /// Executes the specified <paramref name="ifAction"/> if the specified <paramref name="condition"/> is
    /// <see langword="true"/>, otherwise executes the <paramref name="elseAction"/>. This can be used to conditionally add to
    /// the web host builder.
    /// </summary>
    /// <param name="webHostBuilder">The web host builder.</param>
    /// <param name="condition">If <see langword="true"/> is returned the <paramref name="ifAction"/> is executed, otherwise the
    /// <paramref name="elseAction"/> is executed.</param>
    /// <param name="ifAction">The action used to add to the web host builder if the condition is <see langword="true"/>.</param>
    /// <param name="elseAction">The action used to add to the web host builder if the condition is <see langword="false"/>.</param>
    /// <returns>The same web host builder.</returns>
    public static IWebHostBuilder UseIfElse(
        this IWebHostBuilder webHostBuilder,
        Func<IWebHostBuilder, bool> condition,
        Func<IWebHostBuilder, IWebHostBuilder> ifAction,
        Func<IWebHostBuilder, IWebHostBuilder> elseAction
    )
    {
        Argument.IsNotNull(webHostBuilder);
        Argument.IsNotNull(condition);
        Argument.IsNotNull(ifAction);
        Argument.IsNotNull(elseAction);

        return condition(webHostBuilder) ? ifAction(webHostBuilder) : elseAction(webHostBuilder);
    }
}
