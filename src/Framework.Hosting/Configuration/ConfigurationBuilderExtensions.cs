#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Configuration;

/// <summary><see cref="IConfigurationBuilder"/> extension methods.</summary>
[PublicAPI]
public static class ConfigurationBuilderExtensions
{
    /// <summary>
    /// Executes the specified action if the specified <paramref name="condition"/> is <see langword="true"/> which can be
    /// used to conditionally add to the configuration pipeline.
    /// </summary>
    /// <param name="configurationBuilder">The configuration builder.</param>
    /// <param name="condition">If set to <see langword="true"/> the action is executed.</param>
    /// <param name="action">The action used to add to the request execution pipeline.</param>
    /// <returns>The same configuration builder.</returns>
    public static IConfigurationBuilder AddIf(
        this IConfigurationBuilder configurationBuilder,
        bool condition,
        Func<IConfigurationBuilder, IConfigurationBuilder> action
    )
    {
        Argument.IsNotNull(configurationBuilder);
        Argument.IsNotNull(action);

        if (condition)
        {
            configurationBuilder = action(configurationBuilder);
        }

        return configurationBuilder;
    }

    /// <summary>
    /// Executes the specified <paramref name="ifAction"/> if the specified <paramref name="condition"/> is
    /// <see langword="true"/>, otherwise executes the <paramref name="elseAction"/>. This can be used to conditionally add to
    /// the configuration pipeline.
    /// </summary>
    /// <param name="configurationBuilder">The configuration builder.</param>
    /// <param name="condition">If set to <see langword="true"/> the <paramref name="ifAction"/> is executed, otherwise the
    /// <paramref name="elseAction"/> is executed.</param>
    /// <param name="ifAction">The action used to add to the configuration pipeline if the condition is
    /// <see langword="true"/>.</param>
    /// <param name="elseAction">The action used to add to the configuration pipeline if the condition is
    /// <see langword="false"/>.</param>
    /// <returns>The same configuration builder.</returns>
    public static IConfigurationBuilder AddIfElse(
        this IConfigurationBuilder configurationBuilder,
        bool condition,
        Func<IConfigurationBuilder, IConfigurationBuilder> ifAction,
        Func<IConfigurationBuilder, IConfigurationBuilder> elseAction
    )
    {
        Argument.IsNotNull(configurationBuilder);
        Argument.IsNotNull(ifAction);
        Argument.IsNotNull(elseAction);

        return condition ? ifAction(configurationBuilder) : elseAction(configurationBuilder);
    }
}
