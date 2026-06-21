// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Headless.Api.Extensions;

[PublicAPI]
public static class ActionDescriptorExtensions
{
    /// <summary>
    /// Casts <paramref name="actionDescriptor"/> to <see cref="ControllerActionDescriptor"/>.
    /// </summary>
    /// <param name="actionDescriptor">The descriptor to cast.</param>
    /// <returns>The descriptor as a <see cref="ControllerActionDescriptor"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="actionDescriptor"/> is not a <see cref="ControllerActionDescriptor"/>.
    /// Call <see cref="IsControllerAction"/> before casting when the type is not guaranteed.
    /// </exception>
    public static ControllerActionDescriptor AsControllerActionDescriptor(this ActionDescriptor actionDescriptor)
    {
        if (!actionDescriptor.IsControllerAction())
        {
            throw new InvalidOperationException(
                $"{nameof(actionDescriptor)} should be type of {typeof(ControllerActionDescriptor).AssemblyQualifiedName}"
            );
        }

        return (actionDescriptor as ControllerActionDescriptor)!;
    }

    /// <summary>
    /// Returns the <see cref="MethodInfo"/> for the controller action represented by
    /// <paramref name="actionDescriptor"/>.
    /// </summary>
    /// <param name="actionDescriptor">The descriptor to inspect.</param>
    /// <returns>The <see cref="MethodInfo"/> of the controller action method.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="actionDescriptor"/> is not a <see cref="ControllerActionDescriptor"/>.
    /// </exception>
    public static MethodInfo GetMethodInfo(this ActionDescriptor actionDescriptor)
    {
        return actionDescriptor.AsControllerActionDescriptor().MethodInfo;
    }

    /// <summary>
    /// Returns the CLR return type of the controller action method.
    /// </summary>
    /// <param name="actionDescriptor">The descriptor to inspect.</param>
    /// <returns>The return <see cref="Type"/> of the action method.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="actionDescriptor"/> is not a <see cref="ControllerActionDescriptor"/>.
    /// </exception>
    public static Type GetReturnType(this ActionDescriptor actionDescriptor)
    {
        return actionDescriptor.GetMethodInfo().ReturnType;
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="actionDescriptor"/> represents a controller action.</summary>
    /// <param name="actionDescriptor">The descriptor to test.</param>
    public static bool IsControllerAction(this ActionDescriptor actionDescriptor)
    {
        return actionDescriptor is ControllerActionDescriptor;
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="actionDescriptor"/> represents a Razor Pages action.</summary>
    /// <param name="actionDescriptor">The descriptor to test.</param>
    public static bool IsPageAction(this ActionDescriptor actionDescriptor)
    {
        return actionDescriptor is PageActionDescriptor;
    }

    /// <summary>
    /// Casts <paramref name="actionDescriptor"/> to <see cref="PageActionDescriptor"/>.
    /// </summary>
    /// <param name="actionDescriptor">The descriptor to cast.</param>
    /// <returns>The descriptor as a <see cref="PageActionDescriptor"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="actionDescriptor"/> is not a <see cref="PageActionDescriptor"/>.
    /// Call <see cref="IsPageAction"/> before casting when the type is not guaranteed.
    /// </exception>
    public static PageActionDescriptor AsPageAction(this ActionDescriptor actionDescriptor)
    {
        if (!actionDescriptor.IsPageAction())
        {
            throw new InvalidOperationException(
                $"{nameof(actionDescriptor)} should be type of {typeof(PageActionDescriptor).AssemblyQualifiedName}"
            );
        }

        return (actionDescriptor as PageActionDescriptor)!;
    }
}
