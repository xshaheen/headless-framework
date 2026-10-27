// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text;

namespace Headless.OpenApi;

/// <summary>
/// A type as one place in the code uses it: the runtime type plus whether that place annotates each reference type in
/// it, the type arguments included, as nullable.
/// </summary>
/// <remarks>
/// <c>DataEnvelope&lt;User&gt;</c> and <c>DataEnvelope&lt;User?&gt;</c> are one runtime type, so the native generator,
/// which works from runtime types, publishes one schema for both. The annotation survives only in the metadata of the
/// member or return value that uses the type, which <see cref="NullabilityInfoContext"/> reads.
/// </remarks>
internal sealed class TypeUse
{
    private static readonly Dictionary<Type, string> _SimpleTypeNames = new()
    {
        // The names the native generator gives these types inside a generic schema id.
        [typeof(bool)] = "boolean",
        [typeof(byte)] = "byte",
        [typeof(int)] = "int",
        [typeof(uint)] = "uint",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
        [typeof(float)] = "float",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal",
        [typeof(DateTime)] = "DateTime",
        [typeof(DateTimeOffset)] = "DateTimeOffset",
        [typeof(Guid)] = "Guid",
        [typeof(char)] = "char",
        [typeof(Uri)] = "Uri",
        [typeof(string)] = "string",
    };

    private TypeUse(Type type, bool isNullable, IReadOnlyList<TypeUse> arguments)
    {
        Type = type;
        IsNullable = isNullable && !type.IsValueType;
        Arguments = arguments;
    }

    /// <summary>The runtime type.</summary>
    public Type Type { get; }

    /// <summary>Whether the use annotates this reference type as nullable; always <see langword="false"/> for a value type.</summary>
    public bool IsNullable { get; }

    /// <summary>The uses of the generic type arguments, or of the element type of an array, in order.</summary>
    public IReadOnlyList<TypeUse> Arguments { get; }

    /// <summary>
    /// The use of <paramref name="type"/> where no annotation is known, such as <c>typeof(...)</c>: every reference type
    /// in it is non-nullable, as the native generator treats an oblivious type.
    /// </summary>
    public static TypeUse Oblivious(Type type)
    {
        return new TypeUse(type, isNullable: false, [.. _ArgumentTypes(type).Select(Oblivious)]);
    }

    /// <summary>The use <paramref name="info"/> describes.</summary>
    public static TypeUse From(NullabilityInfo info)
    {
        var type = info.Type;
        var argumentTypes = _ArgumentTypes(type);
        TypeUse[] arguments;

        if (type.IsArray && info.ElementType is { } element)
        {
            arguments = [From(element)];
        }
        else if (info.GenericTypeArguments.Length == argumentTypes.Length && !_IsNullableValueType(type))
        {
            arguments = [.. info.GenericTypeArguments.Select(From)];
        }
        else
        {
            // NullabilityInfo describes Nullable<T> by the nullable state alone, so its argument carries no annotation.
            arguments = [.. argumentTypes.Select(Oblivious)];
        }

        return new TypeUse(type, info.ReadState == NullabilityState.Nullable, arguments);
    }

    /// <summary>
    /// The use of <paramref name="type"/> with the given argument uses, for a member whose declared type is built from the
    /// declaring type's parameters.
    /// </summary>
    public static TypeUse Create(Type type, bool isNullable, IReadOnlyList<TypeUse> arguments)
    {
        return new TypeUse(type, isNullable, arguments);
    }

    /// <summary>This use with the given top-level nullability.</summary>
    public TypeUse WithNullable(bool isNullable)
    {
        return isNullable == IsNullable ? this : new TypeUse(Type, isNullable, Arguments);
    }

    /// <summary>This use with every reference type non-nullable, which is how the native generator names the type.</summary>
    public TypeUse WithoutNullableReferences()
    {
        return new TypeUse(
            Type,
            isNullable: false,
            [.. Arguments.Select(argument => argument.WithoutNullableReferences())]
        );
    }

    /// <summary>The first use of <paramref name="type"/> in this use, itself included, in depth-first order.</summary>
    public TypeUse? Find(Type type)
    {
        if (Type == type)
        {
            return this;
        }

        foreach (var argument in Arguments)
        {
            if (argument.Find(type) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>A key that tells two uses apart exactly when their types or annotations differ.</summary>
    public string Key
    {
        get
        {
            var builder = new StringBuilder();
            _AppendKey(builder);

            return builder.ToString();
        }
    }

    /// <summary>
    /// The schema id the native generator's naming gives this use, with <c>Nullable</c> before every argument that is a
    /// nullable reference type or a <see cref="Nullable{T}"/>: <c>DataEnvelopeOfNullableUser</c>,
    /// <c>DataEnvelopeOfNullableint</c>.
    /// </summary>
    /// <remarks>
    /// The native naming drops <see cref="Nullable{T}"/>, so <c>DataEnvelope&lt;int&gt;</c> and
    /// <c>DataEnvelope&lt;int?&gt;</c> would share the id <c>DataEnvelopeOfint</c>, and the second type would silently
    /// reuse the first one's schema.
    /// </remarks>
    public string SchemaId => _Name(this, topLevel: true);

    /// <summary>Whether a generic type argument of <paramref name="type"/>, at any depth, is a <see cref="Nullable{T}"/>.</summary>
    public static bool HasNullableValueArgument(Type type)
    {
        return !_IsNullableValueType(type)
            && _ArgumentTypes(type)
                .Any(argument => _IsNullableValueType(argument) || HasNullableValueArgument(argument));
    }

    private void _AppendKey(StringBuilder builder)
    {
        var type = Type.IsGenericType ? Type.GetGenericTypeDefinition() : Type;
        builder.Append(type.FullName ?? type.Name);

        if (Arguments.Count > 0)
        {
            builder.Append('[');

            for (var i = 0; i < Arguments.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                Arguments[i]._AppendKey(builder);
            }

            builder.Append(']');
        }

        if (IsNullable)
        {
            builder.Append('?');
        }
    }

    private static string _Name(TypeUse use, bool topLevel)
    {
        var type = use.Type;
        var prefix = !topLevel && use.IsNullable ? "Nullable" : "";

        if (_IsNullableValueType(type))
        {
            return "Nullable" + _Name(use.Arguments[0], topLevel: true);
        }

        if (_SimpleTypeNames.TryGetValue(type, out var simple))
        {
            return prefix + simple;
        }

        if (!type.IsGenericType || type.IsArray)
        {
            return prefix + type.Name;
        }

        var name = type.Name.StartsWith("<>f", StringComparison.Ordinal) ? "AnonymousType" : _WithoutArity(type.Name);

        return prefix
            + name
            + "Of"
            + string.Join("And", use.Arguments.Select(argument => _Name(argument, topLevel: false)));
    }

    private static string _WithoutArity(string name)
    {
        var tick = name.LastIndexOf('`');

        return tick == -1 ? name : name[..tick];
    }

    private static Type[] _ArgumentTypes(Type type)
    {
        if (type.IsArray)
        {
            return [type.GetElementType()!];
        }

        return type.IsGenericType ? type.GetGenericArguments() : [];
    }

    private static bool _IsNullableValueType(Type type) => Nullable.GetUnderlyingType(type) is not null;
}
