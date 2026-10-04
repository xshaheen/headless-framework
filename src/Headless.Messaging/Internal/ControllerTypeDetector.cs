// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;

namespace Headless.Messaging.Internal;

/// <summary>Detects MVC-style controller types by convention (public, concrete, non-generic, <c>*Controller</c> name).</summary>
internal static class ControllerTypeDetector
{
    public static bool IsController(TypeInfo typeInfo)
    {
        if (!typeInfo.IsClass)
        {
            return false;
        }

        if (typeInfo.IsAbstract)
        {
            return false;
        }

        if (!typeInfo.IsPublic)
        {
            return false;
        }

        if (typeInfo.ContainsGenericParameters)
        {
            return false;
        }

        return !typeInfo.ContainsGenericParameters
            && typeInfo.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase);
    }
}
