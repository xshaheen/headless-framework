// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;

namespace Framework.Messages.Internal;

public static class Helper
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

    public static bool IsComplexType(Type type)
    {
        return !_CanConvertFromString(type);
    }

    public static string WildcardToRegex(string wildcard)
    {
        const int MaxWildcardLength = 200;
        const int MaxWildcardCount = 10;

        if (wildcard.Length > MaxWildcardLength)
        {
            throw new ArgumentException(
                $"Topic pattern exceeds maximum length of {MaxWildcardLength} characters",
                nameof(wildcard)
            );
        }

        var wildcardCount = wildcard.Count(c => c == '*' || c == '#');
        if (wildcardCount > MaxWildcardCount)
        {
            throw new ArgumentException(
                $"Topic pattern contains too many wildcards (max: {MaxWildcardCount})",
                nameof(wildcard)
            );
        }

        if (wildcard.IndexOf('*') >= 0)
        {
            return ("^" + Regex.Escape(wildcard) + "$")
                .Replace(Regex.Escape("*"), "[0-9a-zA-Z]+?");  // Non-greedy
        }

        if (wildcard.IndexOf('#') >= 0)
        {
            return ("^" + Regex.Escape(wildcard) + "$")
                .Replace(Regex.Escape("#"), "[0-9a-zA-Z\\.]+?");  // Non-greedy
        }

        return Regex.Escape(wildcard);
    }

    public static string? GetInstanceHostname()
    {
        try
        {
            var hostName = Dns.GetHostName();
            if (hostName.Length <= 50)
            {
                return hostName;
            }

            return hostName.Substring(0, 50);
        }
        catch
        {
            return null;
        }
    }

    public static string Normalized(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var pattern = "[\\>\\.\\ \\*]";
        return Regex.IsMatch(name, pattern) ? Regex.Replace(name, pattern, "_") : name;
    }

    public static bool IsUsingType<T>(in Type type)
    {
        var flags =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;
        return type.GetFields(flags).Any(x => x.FieldType == typeof(T));
    }

    public static bool IsInnerIp(string ipAddress)
    {
        var ipNum = _GetIpNum(ipAddress);

        //Private IP：
        //category A: 10.0.0.0-10.255.255.255
        //category B: 172.16.0.0-172.31.255.255
        //category C: 192.168.0.0-192.168.255.255

        var aBegin = _GetIpNum("10.0.0.0");
        var aEnd = _GetIpNum("10.255.255.255");
        var bBegin = _GetIpNum("172.16.0.0");
        var bEnd = _GetIpNum("172.31.255.255");
        var cBegin = _GetIpNum("192.168.0.0");
        var cEnd = _GetIpNum("192.168.255.255");
        return _IsInner(ipNum, aBegin, aEnd) || _IsInner(ipNum, bBegin, bEnd) || _IsInner(ipNum, cBegin, cEnd);
    }

    private static long _GetIpNum(string ipAddress)
    {
        var ip = ipAddress.Split('.');
        long a = int.Parse(ip[0]);
        long b = int.Parse(ip[1]);
        long c = int.Parse(ip[2]);
        long d = int.Parse(ip[3]);

        var ipNum = a * 256 * 256 * 256 + b * 256 * 256 + c * 256 + d;
        return ipNum;
    }

    private static bool _IsInner(long userIp, long begin, long end)
    {
        return userIp >= begin && userIp <= end;
    }

    private static bool _CanConvertFromString(Type destinationType)
    {
        destinationType = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
        return _IsSimpleType(destinationType)
            || TypeDescriptor.GetConverter(destinationType).CanConvertFrom(typeof(string));
    }

    private static bool _IsSimpleType(Type type)
    {
        return type.GetTypeInfo().IsPrimitive
            || type == typeof(decimal)
            || type == typeof(string)
            || type == typeof(DateTime)
            || type == typeof(Guid)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(Uri);
    }

    internal static void ReThrow(this Exception exception)
    {
        ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
