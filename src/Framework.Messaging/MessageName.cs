// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.Domains;

namespace Framework.Messaging;

public static class MessageName
{
    public static string GetFrom<T>()
    {
        return GetFrom(typeof(T));
    }

    public static string GetFrom(Type messageType)
    {
        return messageType.GetCustomAttribute<MessageAttribute>()?.MessageName
            ?? $"{messageType.FullName}, {messageType.Assembly.GetName().Name}";
    }
}
