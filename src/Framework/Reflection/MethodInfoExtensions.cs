// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

        return method.ReturnType.IsTaskOrTaskOfT();
    }
}
