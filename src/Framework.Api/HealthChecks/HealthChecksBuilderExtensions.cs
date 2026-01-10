// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary><see cref="IHealthChecksBuilder"/> extension methods.</summary>
[PublicAPI]
public static class HealthChecksBuilderExtensions
{
    /// <summary>
    /// Executes the specified action if the specified <paramref name="condition"/> is <see langword="true"/> which can be
    /// used to conditionally add to the health check builder.
    /// </summary>
    /// <param name="healthChecksBuilder">The health checks builder.</param>
    /// <param name="condition">If set to <see langword="true"/> the action is executed.</param>
    /// <param name="action">The action used to add to the health check builder.</param>
    /// <returns>The same health checks builder.</returns>
    public static IHealthChecksBuilder AddIf(
        this IHealthChecksBuilder healthChecksBuilder,
        bool condition,
        Func<IHealthChecksBuilder, IHealthChecksBuilder> action
    )
    {
        Argument.IsNotNull(healthChecksBuilder);
        Argument.IsNotNull(action);

        if (condition)
        {
            healthChecksBuilder = action(healthChecksBuilder);
        }

        return healthChecksBuilder;
    }

    /// <summary>
    /// Executes the specified <paramref name="ifAction"/> if the specified <paramref name="condition"/> is
    /// <see langword="true"/>, otherwise executes the <paramref name="elseAction"/>. This can be used to conditionally add to
    /// the health check builder.
    /// </summary>
    /// <param name="healthChecksBuilder">The health checks builder.</param>
    /// <param name="condition">If set to <see langword="true"/> the <paramref name="ifAction"/> is executed, otherwise the
    /// <paramref name="elseAction"/> is executed.</param>
    /// <param name="ifAction">The action used to add to the health check builder if the condition is <see langword="true"/>.</param>
    /// <param name="elseAction">The action used to add to the health check builder if the condition is <see langword="false"/>.</param>
    /// <returns>The same health checks builder.</returns>
    public static IHealthChecksBuilder AddIfElse(
        this IHealthChecksBuilder healthChecksBuilder,
        bool condition,
        Func<IHealthChecksBuilder, IHealthChecksBuilder> ifAction,
        Func<IHealthChecksBuilder, IHealthChecksBuilder> elseAction
    )
    {
        Argument.IsNotNull(healthChecksBuilder);
        Argument.IsNotNull(ifAction);
        Argument.IsNotNull(elseAction);

        return condition ? ifAction(healthChecksBuilder) : elseAction(healthChecksBuilder);
    }
}
