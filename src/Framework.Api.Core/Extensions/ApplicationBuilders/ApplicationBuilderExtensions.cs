using Framework.Arguments;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="IApplicationBuilder"/> extension methods.</summary>
public static class ApplicationBuilderHelperExtensions
{
    /// <summary>
    /// Executes the specified action if the specified <paramref name="condition"/> is <see langword="true"/> which can be
    /// used to conditionally add to the request execution pipeline.
    /// </summary>
    /// <param name="application">The application builder.</param>
    /// <param name="condition">If set to <see langword="true"/> the action is executed.</param>
    /// <param name="action">The action used to add to the request execution pipeline.</param>
    /// <returns>The same application builder.</returns>
    public static WebApplication UseIf(
        this WebApplication application,
        bool condition,
        Func<WebApplication, WebApplication> action
    )
    {
        Argument.IsNotNull(application);
        Argument.IsNotNull(action);

        if (condition)
        {
            application = action(application);
        }

        return application;
    }

    /// <summary>
    /// Executes the specified <paramref name="ifAction"/> if the specified <paramref name="condition"/> is
    /// <see langword="true"/>, otherwise executes the <paramref name="elseAction"/>. This can be used to conditionally add to
    /// the request execution pipeline.
    /// </summary>
    /// <param name="application">The application builder.</param>
    /// <param name="condition">If set to <see langword="true"/> the <paramref name="ifAction"/> is executed, otherwise the
    /// <paramref name="elseAction"/> is executed.</param>
    /// <param name="ifAction">The action used to add to the request execution pipeline if the condition is
    /// <see langword="true"/>.</param>
    /// <param name="elseAction">The action used to add to the request execution pipeline if the condition is
    /// <see langword="false"/>.</param>
    /// <returns>The same application builder.</returns>
    public static WebApplication UseIfElse(
        this WebApplication application,
        bool condition,
        Func<WebApplication, WebApplication> ifAction,
        Func<WebApplication, WebApplication> elseAction
    )
    {
        Argument.IsNotNull(application);
        Argument.IsNotNull(ifAction);
        Argument.IsNotNull(elseAction);

        return condition ? ifAction(application) : elseAction(application);
    }

    /// <summary>
    /// Executes the specified <paramref name="ifAction"/> if the specified <paramref name="condition"/> is
    /// <see langword="true"/>, otherwise executes the <paramref name="elseAction"/>. This can be used to conditionally add to
    /// the request execution pipeline.
    /// </summary>
    /// <param name="application">The application builder.</param>
    /// <param name="condition">If set to <see langword="true"/> the <paramref name="ifAction"/> is executed, otherwise the
    /// <paramref name="elseAction"/> is executed.</param>
    /// <param name="ifAction">The action used to add to the request execution pipeline if the condition is
    /// <see langword="true"/>.</param>
    /// <param name="elseAction">The action used to add to the request execution pipeline if the condition is
    /// <see langword="false"/>.</param>
    /// <returns>The same application builder.</returns>
    public static T UseIfElse<T>(
        this WebApplication application,
        bool condition,
        Func<WebApplication, T> ifAction,
        Func<WebApplication, T> elseAction
    )
    {
        Argument.IsNotNull(application);
        Argument.IsNotNull(ifAction);
        Argument.IsNotNull(elseAction);

        return condition ? ifAction(application) : elseAction(application);
    }

    /// <summary>
    /// Executes the specified action if the specified <paramref name="condition"/> is <see langword="true"/> which can be
    /// used to conditionally add to the request execution pipeline.
    /// </summary>
    /// <param name="application">The application builder.</param>
    /// <param name="condition">If set to <see langword="true"/> the action is executed.</param>
    /// <param name="action">The action used to add to the request execution pipeline.</param>
    /// <returns>The same application builder.</returns>
    public static IApplicationBuilder UseIf(
        this IApplicationBuilder application,
        bool condition,
        Func<IApplicationBuilder, IApplicationBuilder> action
    )
    {
        Argument.IsNotNull(application);
        Argument.IsNotNull(action);

        if (condition)
        {
            application = action(application);
        }

        return application;
    }

    /// <summary>
    /// Executes the specified <paramref name="ifAction"/> if the specified <paramref name="condition"/> is
    /// <see langword="true"/>, otherwise executes the <paramref name="elseAction"/>. This can be used to conditionally add to
    /// the request execution pipeline.
    /// </summary>
    /// <param name="application">The application builder.</param>
    /// <param name="condition">If set to <see langword="true"/> the <paramref name="ifAction"/> is executed, otherwise the
    /// <paramref name="elseAction"/> is executed.</param>
    /// <param name="ifAction">The action used to add to the request execution pipeline if the condition is
    /// <see langword="true"/>.</param>
    /// <param name="elseAction">The action used to add to the request execution pipeline if the condition is
    /// <see langword="false"/>.</param>
    /// <returns>The same application builder.</returns>
    public static IApplicationBuilder UseIfElse(
        this IApplicationBuilder application,
        bool condition,
        Func<IApplicationBuilder, IApplicationBuilder> ifAction,
        Func<IApplicationBuilder, IApplicationBuilder> elseAction
    )
    {
        Argument.IsNotNull(application);
        Argument.IsNotNull(ifAction);
        Argument.IsNotNull(elseAction);

        return condition ? ifAction(application) : elseAction(application);
    }

    /// <summary>
    /// Executes the specified <paramref name="ifAction"/> if the specified <paramref name="condition"/> is
    /// <see langword="true"/>, otherwise executes the <paramref name="elseAction"/>. This can be used to conditionally add to
    /// the request execution pipeline.
    /// </summary>
    /// <param name="application">The application builder.</param>
    /// <param name="condition">If set to <see langword="true"/> the <paramref name="ifAction"/> is executed, otherwise the
    /// <paramref name="elseAction"/> is executed.</param>
    /// <param name="ifAction">The action used to add to the request execution pipeline if the condition is
    /// <see langword="true"/>.</param>
    /// <param name="elseAction">The action used to add to the request execution pipeline if the condition is
    /// <see langword="false"/>.</param>
    /// <returns>The same application builder.</returns>
    public static T UseIfElse<T>(
        this IApplicationBuilder application,
        bool condition,
        Func<IApplicationBuilder, T> ifAction,
        Func<IApplicationBuilder, T> elseAction
    )
    {
        Argument.IsNotNull(application);
        Argument.IsNotNull(ifAction);
        Argument.IsNotNull(elseAction);

        return condition ? ifAction(application) : elseAction(application);
    }
}
