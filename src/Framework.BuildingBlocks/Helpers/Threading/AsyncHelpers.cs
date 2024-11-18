// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Nito.AsyncEx;

namespace Framework.BuildingBlocks.Helpers.Threading;

[PublicAPI]
public static class AsyncHelpers
{
    /// <summary>Runs a async method synchronously.</summary>
    /// <param name="action">An async action</param>
    public static void RunSync(Func<Task> action)
    {
        AsyncContext.Run(action);
    }

    /// <summary>Runs a async method synchronously.</summary>
    /// <param name="func">A function that returns a result</param>
    /// <typeparam name="TResult">Result type</typeparam>
    /// <returns>Result of the async operation</returns>
    public static TResult RunSync<TResult>(Func<Task<TResult>> func)
    {
        return AsyncContext.Run(func);
    }

    /// <summary>Returns void if given type is Task. Return T, if given type is Task{T}. Returns given type otherwise.</summary>
    public static Type UnwrapTask(Type type)
    {
        Argument.IsNotNull(type);

        if (type == typeof(Task))
        {
            return typeof(void);
        }

        if (type.IsTaskOfT())
        {
            return type.GenericTypeArguments[0];
        }

        return type;
    }
}
