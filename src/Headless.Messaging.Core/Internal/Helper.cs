// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Headless.Messaging.Internal;

public static partial class Helper
{
    private static readonly ConcurrentDictionary<Type, TypeConverter> _ConverterCache = new();

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
        const int maxWildcardLength = 200;
        const int maxWildcardCount = 10;

        if (wildcard.Length > maxWildcardLength)
        {
            throw new ArgumentException(
                $@"Topic pattern exceeds maximum length of {maxWildcardLength} characters",
                nameof(wildcard)
            );
        }

        var wildcardCount = wildcard.Count(c => c is '*' or '#');
        if (wildcardCount > maxWildcardCount)
        {
            throw new ArgumentException(
                $@"Topic pattern contains too many wildcards (max: {maxWildcardCount})",
                nameof(wildcard)
            );
        }

        if (wildcard.Contains('*'))
        {
            // Possessive quantifier (atomic group) prevents backtracking entirely
            return ("^" + Regex.Escape(wildcard) + "$").Replace(Regex.Escape("*"), "(?>[0-9a-zA-Z]+)");
        }

        if (wildcard.Contains('#'))
        {
            // Possessive quantifier (atomic group) prevents backtracking entirely
            return ("^" + Regex.Escape(wildcard) + "$").Replace(Regex.Escape("#"), "(?>[0-9a-zA-Z\\.]+)");
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

            return hostName[..50];
        }
#pragma warning disable ERP022
        catch
        {
            return null;
        }
#pragma warning restore ERP022
    }

    [GeneratedRegex("[\\>\\.\\ \\*]", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, 100)]
    private static partial Regex NormalizedRegex { get; }

    public static string Normalized(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        return NormalizedRegex.IsMatch(name) ? NormalizedRegex.Replace(name, "_") : name;
    }

    public static bool IsUsingType<T>(in Type type)
    {
        const BindingFlags flags =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        return type.GetFields(flags).Any(x => x.FieldType == typeof(T));
    }

    public static bool IsInnerIp(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return false;
        }

        // Ensure proper IPv4 format (must have exactly 3 dots)
        var octets = ipAddress.Split('.');
        if (octets.Length != 4)
        {
            return false;
        }

        if (
            !IPAddress.TryParse(ipAddress, out var ip)
            || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
        )
        {
            return false;
        }

        //Private IP：
        //category A: 10.0.0.0-10.255.255.255
        //category B: 172.16.0.0-172.31.255.255
        //category C: 192.168.0.0-192.168.255.255

        var bytes = ip.GetAddressBytes();
        var first = bytes[0];
        var second = bytes[1];

        // Class A: 10.0.0.0/8
        if (first == 10)
        {
            return true;
        }

        // Class B: 172.16.0.0/12
        if (first == 172 && second is >= 16 and <= 31)
        {
            return true;
        }

        // Class C: 192.168.0.0/16
        if (first == 192 && second == 168)
        {
            return true;
        }

        return false;
    }

    private static bool _CanConvertFromString(Type destinationType)
    {
        destinationType = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
        if (_IsSimpleType(destinationType))
        {
            return true;
        }

        var converter = _ConverterCache.GetOrAdd(destinationType, TypeDescriptor.GetConverter);
        return converter.CanConvertFrom(typeof(string));
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
}
