// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System.Reflection;

#pragma warning disable RCS1047 // Remove Async suffix.
/// <summary>
/// Extension methods over <see cref="MethodInfo"/>.
/// </summary>
[PublicAPI]
public static class HeadlessMethodInfoExtensions
{
    /// <summary>Checks if given method is an async method.</summary>
    /// <param name="method">A method to check</param>
    /// <returns>
    /// <see langword="true"/> if the method returns <see cref="Task"/>, <see cref="ValueTask"/>, their generic forms,
    /// <see cref="IAsyncEnumerable{T}"/>, <see cref="IAsyncEnumerator{T}"/>, or is marked with
    /// <see cref="System.Runtime.CompilerServices.AsyncStateMachineAttribute"/>; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="method"/> is <see langword="null"/>.</exception>
    [MustUseReturnValue]
    public static bool IsAsync(this MethodInfo method)
    {
        Argument.IsNotNull(method);

        if (!method.ReturnType.IsGenericType)
        {
            return method.ReturnType == typeof(Task)
                || method.ReturnType == typeof(ValueTask)
                || Attribute.IsDefined(method, typeof(AsyncStateMachineAttribute));
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
        return Attribute.IsDefined(method, typeof(AsyncStateMachineAttribute));
    }
}
