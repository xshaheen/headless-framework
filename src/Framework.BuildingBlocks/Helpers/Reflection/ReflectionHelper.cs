// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;

namespace Framework.BuildingBlocks.Helpers.Reflection;

[PublicAPI]
public static class ReflectionHelper
{
    /// <summary>
    /// Tries to gets an of attribute defined for a class member and it's declaring type including
    /// inherited attributes.
    /// Returns default value if it's not declared at all.
    /// </summary>
    /// <typeparam name="TAttribute">Type of the attribute</typeparam>
    /// <param name="memberInfo">MemberInfo</param>
    /// <param name="defaultValue">Default value (null as default)</param>
    /// <param name="inherit">Inherit attribute from base classes</param>
    public static TAttribute? GetFirstOrDefaultAttribute<TAttribute>(
        this MemberInfo memberInfo,
        TAttribute? defaultValue = default,
        bool inherit = true
    )
        where TAttribute : Attribute
    {
        return memberInfo.IsDefined(typeof(TAttribute), inherit)
            ? memberInfo.GetCustomAttributes(typeof(TAttribute), inherit).Cast<TAttribute>().First()
            : defaultValue;
    }

    /// <summary>
    /// Tries to gets an of attribute defined for a class member and it's declaring type including
    /// inherited attributes.
    /// Returns default value if it's not declared at all.
    /// </summary>
    /// <typeparam name="TAttribute">Type of the attribute</typeparam>
    /// <param name="memberInfo">MemberInfo</param>
    /// <param name="inherit">Inherit attribute from base classes</param>
    public static TAttribute? GetFirstAttribute<TAttribute>(this MemberInfo memberInfo, bool inherit = true)
        where TAttribute : Attribute
    //Get attribute on the member
    {
        return memberInfo.GetCustomAttributes(typeof(TAttribute), inherit).Cast<TAttribute>().First();
    }

    /// <summary>
    /// Tries to gets an of attribute defined for a class member and it's declaring type including
    /// inherited attributes.
    /// Returns default value if it's not declared at all.
    /// </summary>
    /// <typeparam name="TAttribute">Type of the attribute</typeparam>
    /// <param name="memberInfo">MemberInfo</param>
    /// <param name="defaultValue">Default value (null as default)</param>
    /// <param name="inherit">Inherit attribute from base classes</param>
    public static TAttribute? GetSingleAttributeOfMemberOrDeclaringTypeOrDefault<TAttribute>(
        this MemberInfo memberInfo,
        TAttribute? defaultValue = default,
        bool inherit = true
    )
        where TAttribute : class
    {
        return memberInfo.GetCustomAttributes(inherit).OfType<TAttribute>().FirstOrDefault()
            ?? memberInfo
                .DeclaringType?.GetTypeInfo()
                .GetCustomAttributes(inherit)
                .OfType<TAttribute>()
                .FirstOrDefault()
            ?? defaultValue;
    }

    /// <summary>
    /// Tries to gets attributes defined for a class member and it's declaring type including inherited
    /// attributes.
    /// </summary>
    /// <typeparam name="TAttribute">Type of the attribute</typeparam>
    /// <param name="memberInfo">MemberInfo</param>
    /// <param name="inherit">Inherit attribute from base classes</param>
    public static IEnumerable<TAttribute> GetAttributesOfMemberOrDeclaringType<TAttribute>(
        this MemberInfo memberInfo,
        bool inherit = true
    )
        where TAttribute : class
    {
        var customAttributes = memberInfo.GetCustomAttributes(inherit).OfType<TAttribute>();

        var declaringTypeCustomAttributes = memberInfo
            .DeclaringType?.GetTypeInfo()
            .GetCustomAttributes(inherit)
            .OfType<TAttribute>();

        return declaringTypeCustomAttributes is not null
            ? customAttributes.Concat(declaringTypeCustomAttributes).Distinct()
            : customAttributes;
    }

    public static bool IsNullableOfT(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return Nullable.GetUnderlyingType(type) is not null;
    }

    public static bool IsFlagsEnum<T>()
    {
        return IsFlagsEnum(typeof(T));
    }

    public static bool IsFlagsEnum(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!type.IsEnum)
        {
            return false;
        }

        return type.IsDefined(typeof(FlagsAttribute), inherit: true);
    }

    public static bool IsSubClassOfGeneric(this Type child, Type parent)
    {
        if (child == parent)
        {
            return false;
        }

        if (child.IsSubclassOf(parent))
        {
            return true;
        }

        var parameters = parent.GetGenericArguments();

        var isParameterLessGeneric = !(
            parameters is { Length: > 0 }
            && (parameters[0].Attributes & TypeAttributes.BeforeFieldInit) == TypeAttributes.BeforeFieldInit
        );

        while (child is not null && child != typeof(object))
        {
            var cur = _GetFullTypeDefinition(child);

            if (
                parent == cur
                || (
                    isParameterLessGeneric
                    && cur.GetInterfaces().Select(_GetFullTypeDefinition).Contains(_GetFullTypeDefinition(parent))
                )
            )
            {
                return true;
            }

            if (!isParameterLessGeneric)
            {
                if (_GetFullTypeDefinition(parent) == cur && !cur.IsInterface)
                {
                    if (_VerifyGenericArguments(_GetFullTypeDefinition(parent), cur))
                    {
                        if (_VerifyGenericArguments(parent, child))
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    foreach (
                        var item in child
                            .GetInterfaces()
                            .Where(i => _GetFullTypeDefinition(parent) == _GetFullTypeDefinition(i))
                    )
                    {
                        if (_VerifyGenericArguments(parent, item))
                        {
                            return true;
                        }
                    }
                }
            }

            child = child.BaseType!;
        }

        return false;
    }

    private static Type _GetFullTypeDefinition(Type type)
    {
        return type.IsGenericType ? type.GetGenericTypeDefinition() : type;
    }

    private static bool _VerifyGenericArguments(Type parent, Type child)
    {
        var childArguments = child.GetGenericArguments();
        var parentArguments = parent.GetGenericArguments();

        if (childArguments.Length != parentArguments.Length)
        {
            return true;
        }

        for (var i = 0; i < childArguments.Length; i++)
        {
            if (
                childArguments[i].Assembly == parentArguments[i].Assembly
                && string.Equals(childArguments[i].Name, parentArguments[i].Name, StringComparison.Ordinal)
                && string.Equals(childArguments[i].Namespace, parentArguments[i].Namespace, StringComparison.Ordinal)
            )
            {
                continue;
            }

            if (!childArguments[i].IsSubclassOf(parentArguments[i]))
            {
                return false;
            }
        }

        return true;
    }
}
