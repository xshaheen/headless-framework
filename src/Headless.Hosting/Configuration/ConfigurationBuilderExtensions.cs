// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Configuration;

/// <summary><see cref="IConfigurationBuilder"/> extension methods.</summary>
[PublicAPI]
public static class ConfigurationBuilderExtensions
{
    extension(IConfigurationBuilder configurationBuilder)
    {
        /// <summary>
        /// Executes the specified action if the specified <paramref name="condition"/> is <see langword="true"/> which can be
        /// used to conditionally add to the configuration pipeline.
        /// </summary>
        /// <param name="condition">If set to <see langword="true"/> the action is executed.</param>
        /// <param name="action">The action used to add to the request execution pipeline.</param>
        /// <returns>The same configuration builder.</returns>
        public IConfigurationBuilder AddIf(bool condition, Func<IConfigurationBuilder, IConfigurationBuilder> action)
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
        /// Executes the specified action if the specified <paramref name="condition"/> is <see langword="true"/> which can be
        /// used to conditionally add to the configuration pipeline.
        /// </summary>
        /// <param name="condition">If <see langword="true"/> is returned the action is executed.</param>
        /// <param name="action">The action used to add to the request execution pipeline.</param>
        /// <returns>The same configuration builder.</returns>
        public IConfigurationBuilder AddIf(
            Func<IConfigurationBuilder, bool> condition,
            Func<IConfigurationBuilder, IConfigurationBuilder> action
        )
        {
            Argument.IsNotNull(configurationBuilder);
            Argument.IsNotNull(condition);
            Argument.IsNotNull(action);

            if (condition(configurationBuilder))
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
        /// <param name="condition">If set to <see langword="true"/> the <paramref name="ifAction"/> is executed, otherwise the
        /// <paramref name="elseAction"/> is executed.</param>
        /// <param name="ifAction">The action used to add to the configuration pipeline if the condition is
        /// <see langword="true"/>.</param>
        /// <param name="elseAction">The action used to add to the configuration pipeline if the condition is
        /// <see langword="false"/>.</param>
        /// <returns>The same configuration builder.</returns>
        public IConfigurationBuilder AddIfElse(
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

        /// <summary>
        /// Executes the specified <paramref name="ifAction"/> if the specified <paramref name="condition"/> is
        /// <see langword="true"/>, otherwise executes the <paramref name="elseAction"/>. This can be used to conditionally add to
        /// the configuration pipeline.
        /// </summary>
        /// <param name="condition">If <see langword="true"/> is returned the <paramref name="ifAction"/> is executed, otherwise the
        /// <paramref name="elseAction"/> is executed.</param>
        /// <param name="ifAction">The action used to add to the configuration pipeline if the condition is
        /// <see langword="true"/>.</param>
        /// <param name="elseAction">The action used to add to the configuration pipeline if the condition is
        /// <see langword="false"/>.</param>
        /// <returns>The same configuration builder.</returns>
        public IConfigurationBuilder AddIfElse(
            Func<IConfigurationBuilder, bool> condition,
            Func<IConfigurationBuilder, IConfigurationBuilder> ifAction,
            Func<IConfigurationBuilder, IConfigurationBuilder> elseAction
        )
        {
            Argument.IsNotNull(configurationBuilder);
            Argument.IsNotNull(condition);
            Argument.IsNotNull(ifAction);
            Argument.IsNotNull(elseAction);

            return condition(configurationBuilder) ? ifAction(configurationBuilder) : elseAction(configurationBuilder);
        }
    }
}
