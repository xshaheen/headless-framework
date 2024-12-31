// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Framework.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Reflection;

#pragma warning disable RCS1047 // Remove Async suffix.
public static class MethodInfoExtensions
{
    /// <summary>Checks if given method is an async method.</summary>
    /// <param name="method">A method to check</param>
    [MustUseReturnValue]
    public static bool IsAsync(this MethodInfo method)
    {
        Argument.IsNotNull(method);

        if (!method.ReturnType.IsGenericType)
        {
            return method.ReturnType == typeof(Task)
                || method.ReturnType == typeof(ValueTask)
                || method.GetCustomAttribute<AsyncStateMachineAttribute>() != null;
        }

        var genericTypeDefinition = method.ReturnType.GetGenericTypeDefinition();

        if (
            genericTypeDefinition == typeof(Task<>)
            || genericTypeDefinition == typeof(ValueTask<>)
            || genericTypeDefinition == typeof(IAsyncEnumerable<>)
            || genericTypeDefinition == typeof(IAsyncEnumerator<>)
        )
        {
            return true;
        }

        // Fallback to check for AsyncStateMachineAttribute if it has async/await keywords
        return method.GetCustomAttribute<AsyncStateMachineAttribute>() != null;
    }
}
