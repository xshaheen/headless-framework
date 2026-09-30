// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Runtime;

namespace Headless.Messaging.Dashboard;

internal static class HtmlHelper
{
    /// <summary>
    /// Renders the handler a consumer runs as a highlighted C# signature: <c>ConsumeAsync</c> taking the typed consume
    /// context for an attribute-declared consumer, or the delegate method of a runtime subscription.
    /// </summary>
    public static string MethodEscaped(string methodName, Type? messageType)
    {
        var context = messageType is null
            ? _WrapType(nameof(ConsumeContext))
            : _WrapType(nameof(ConsumeContext)) + _WrapIdentifier("<") + _WrapType(messageType) + _WrapIdentifier(">");

        return $"{_WrapKeyword("public")} {_WrapType(nameof(ValueTask))} {methodName}({context} context);";
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

        if (RuntimeTypeInspection.IsComplexType(type))
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
