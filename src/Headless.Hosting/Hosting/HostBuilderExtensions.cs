// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting;

/// <summary>
/// <see cref="IHostBuilder"/> extension methods.
/// </summary>
[PublicAPI]
public static class HostBuilderHelperExtensions
{
    extension(IHostBuilder hostBuilder)
    {
        /// <summary>
        /// Executes the specified action if the specified <paramref name="condition"/> is <see langword="true"/> which can be
        /// used to conditionally add to the host builder.
        /// </summary>
        /// <param name="condition">If set to <see langword="true"/> the action is executed.</param>
        /// <param name="action">The action used to add to the host builder.</param>
        /// <returns>The same host builder.</returns>
        public IHostBuilder UseIf(bool condition, Func<IHostBuilder, IHostBuilder> action)
        {
            Argument.IsNotNull(hostBuilder);
            Argument.IsNotNull(action);

            if (condition)
            {
                hostBuilder = action(hostBuilder);
            }

            return hostBuilder;
        }

        /// <summary>
        /// Executes the specified action if the specified <paramref name="condition"/> is <see langword="true"/> which can be
        /// used to conditionally add to the host builder.
        /// </summary>
        /// <param name="condition">If <see langword="true"/> is returned the action is executed.</param>
        /// <param name="action">The action used to add to the host builder.</param>
        /// <returns>The same host builder.</returns>
        public IHostBuilder UseIf(Func<IHostBuilder, bool> condition, Func<IHostBuilder, IHostBuilder> action)
        {
            Argument.IsNotNull(hostBuilder);
            Argument.IsNotNull(condition);
            Argument.IsNotNull(action);

            if (condition(hostBuilder))
            {
                hostBuilder = action(hostBuilder);
            }

            return hostBuilder;
        }

        /// <summary>
        /// Executes the specified <paramref name="ifAction"/> if the specified <paramref name="condition"/> is
        /// <see langword="true"/>, otherwise executes the <paramref name="elseAction"/>. This can be used to conditionally add to
        /// the host builder.
        /// </summary>
        /// <param name="condition">If set to <see langword="true"/> the <paramref name="ifAction"/> is executed, otherwise the
        /// <paramref name="elseAction"/> is executed.</param>
        /// <param name="ifAction">The action used to add to the host builder if the condition is <see langword="true"/>.</param>
        /// <param name="elseAction">The action used to add to the host builder if the condition is <see langword="false"/>.</param>
        /// <returns>The same host builder.</returns>
        public IHostBuilder UseIfElse(
            bool condition,
            Func<IHostBuilder, IHostBuilder> ifAction,
            Func<IHostBuilder, IHostBuilder> elseAction
        )
        {
            Argument.IsNotNull(hostBuilder);
            Argument.IsNotNull(ifAction);
            Argument.IsNotNull(elseAction);

            return condition ? ifAction(hostBuilder) : elseAction(hostBuilder);
        }

        /// <summary>
        /// Executes the specified <paramref name="ifAction"/> if the specified <paramref name="condition"/> is
        /// <see langword="true"/>, otherwise executes the <paramref name="elseAction"/>. This can be used to conditionally add to
        /// the host builder.
        /// </summary>
        /// <param name="condition">If <see langword="true"/> is returned the <paramref name="ifAction"/> is executed, otherwise the
        /// <paramref name="elseAction"/> is executed.</param>
        /// <param name="ifAction">The action used to add to the host builder if the condition is <see langword="true"/>.</param>
        /// <param name="elseAction">The action used to add to the host builder if the condition is <see langword="false"/>.</param>
        /// <returns>The same host builder.</returns>
        public IHostBuilder UseIfElse(
            Func<IHostBuilder, bool> condition,
            Func<IHostBuilder, IHostBuilder> ifAction,
            Func<IHostBuilder, IHostBuilder> elseAction
        )
        {
            Argument.IsNotNull(hostBuilder);
            Argument.IsNotNull(condition);
            Argument.IsNotNull(ifAction);
            Argument.IsNotNull(elseAction);

            return condition(hostBuilder) ? ifAction(hostBuilder) : elseAction(hostBuilder);
        }
    }
}
