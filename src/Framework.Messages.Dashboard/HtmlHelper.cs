// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.CompilerServices;
using Framework.Messages.Internal;

namespace Framework.Messages;

public static class HtmlHelper
{
    public static string MethodEscaped(MethodInfo method)
    {
        var @public = _WrapKeyword("public");
        var async = string.Empty;
        string @return;

        var isAwaitable = _IsTypeAwaitable(method.ReturnType, out var resultType);
        if (isAwaitable)
        {
            async = _WrapKeyword("async");
            if (resultType == typeof(void))
            {
                @return = _WrapType("Task");
            }
            else
            {
                @return = _WrapType("Task") + _WrapIdentifier("<") + _WrapType(resultType) + _WrapIdentifier(">");
            }
        }
        else
        {
            @return = _WrapType(method.ReturnType);
        }

        var name = method.Name;

        string? paramType = null;
        string? paramName = null;

        var @params = method.GetParameters();
        if (@params.Length == 1)
        {
            var firstParam = @params[0];
            var firstParamType = firstParam.ParameterType;
            paramType = _WrapType(firstParamType);
            paramName = firstParam.Name;
        }

        var paramString = paramType == null ? "();" : $"({paramType} {paramName});";

        var outputString =
            @public + " " + (string.IsNullOrEmpty(async) ? "" : async + " ") + @return + " " + name + paramString;

        return outputString;
    }

    /// <summary>
    /// Checks if a type is awaitable and returns its result type.
    /// Based on Roslyn's awaitable pattern detection.
    /// </summary>
    private static bool _IsTypeAwaitable(Type type, out Type resultType)
    {
        // Check for GetAwaiter method
        var getAwaiterMethod = type.GetRuntimeMethods()
            .FirstOrDefault(m =>
                m.Name.Equals("GetAwaiter", StringComparison.OrdinalIgnoreCase)
                && m.GetParameters().Length == 0
                && m.ReturnType != null
            );

        if (getAwaiterMethod == null)
        {
            resultType = typeof(void);
            return false;
        }

        var awaiterType = getAwaiterMethod.ReturnType;

        // Awaiter must implement INotifyCompletion
        if (awaiterType.GetInterfaces().All(t => t != typeof(INotifyCompletion)))
        {
            resultType = typeof(void);
            return false;
        }

        // Awaiter must have GetResult method
        var getResultMethod = awaiterType
            .GetRuntimeMethods()
            .FirstOrDefault(m => m.Name.Equals("GetResult", StringComparison.Ordinal) && m.GetParameters().Length == 0);

        if (getResultMethod == null)
        {
            resultType = typeof(void);
            return false;
        }

        resultType = getResultMethod.ReturnType;
        return true;
    }

    private static string _WrapType(Type? type)
    {
        if (type == null)
        {
            return string.Empty;
        }

        if (string.Equals(type.Name, "Void", StringComparison.Ordinal))
        {
            return _WrapKeyword(type.Name.ToLowerInvariant());
        }

        if (Helper.IsComplexType(type))
        {
            return _WrapType(type.Name);
        }

        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
        {
            return _WrapKeyword(type.Name.ToLowerInvariant());
        }

        return _WrapType(type.Name);
    }

    private static string _WrapIdentifier(string value)
    {
        return value;
    }

    private static string _WrapKeyword(string value)
    {
        return _Span("keyword", value);
    }

    private static string _WrapType(string value)
    {
        return _Span("type", value);
    }

    private static string _Span(string @class, string value)
    {
        return $"<span class=\"{@class}\">{value}</span>";
    }
}
