// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.Messages.Internal;
using Framework.Messages.Internal.ObjectMethodExecutor;
using Microsoft.Extensions.Internal;

namespace DotNetCore.CAP.Dashboard;

public class HtmlHelper
{
    public static string MethodEscaped(MethodInfo method)
    {
        var @public = _WrapKeyword("public");
        var async = string.Empty;
        string @return;

        var isAwaitable = CoercedAwaitableInfo.IsTypeAwaitable(method.ReturnType, out var coercedAwaitableInfo);
        if (isAwaitable)
        {
            async = _WrapKeyword("async");
            var asyncResultType = coercedAwaitableInfo.AwaitableInfo.ResultType;
            if (asyncResultType.Name == "Void")
                @return = _WrapType("Task");
            else
                @return = _WrapType("Task") + _WrapIdentifier("<") + _WrapType(asyncResultType) + _WrapIdentifier(">");
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

    private static string _WrapType(Type type)
    {
        if (type == null)
            return string.Empty;

        if (type.Name == "Void")
            return _WrapKeyword(type.Name.ToLower());

        if (Helper.IsComplexType(type))
            return _WrapType(type.Name);

        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
            return _WrapKeyword(type.Name.ToLower());

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
