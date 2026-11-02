// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using System.Reflection;
using Headless.Generator.Primitives;
using MessagePack;
using MessagePack.Formatters;

namespace Headless.Serializer;

/// <summary>
/// MessagePack resolver that writes a generated primitive (an <see cref="IPrimitive{T}"/>) as its underlying value and
/// reads it back through the primitive's value constructor, so validation runs on read.
/// </summary>
/// <remarks>
/// A generated primitive keeps its value in private fields. A member-mapping resolver such as
/// <c>ContractlessStandardResolver</c> sees no public members, writes an empty map, and reads back an uninitialized
/// value that throws on first use. <see cref="MessagePackBinarySerializer"/> puts this resolver ahead of the
/// contractless one by default; compose it the same way when you supply your own
/// <see cref="MessagePackSerializerOptions"/>:
/// <c>CompositeResolver.Create(PrimitiveFormatterResolver.Instance, ContractlessStandardResolver.Instance)</c>.
/// </remarks>
[PublicAPI]
[RequiresDynamicCode("Builds a formatter per primitive type at run time.")]
[RequiresUnreferencedCode("Finds the primitive's value constructor through reflection.")]
public sealed class PrimitiveFormatterResolver : IFormatterResolver
{
    /// <summary>The shared resolver instance.</summary>
    public static readonly PrimitiveFormatterResolver Instance = new();

    private PrimitiveFormatterResolver() { }

    /// <inheritdoc/>
    public IMessagePackFormatter<T>? GetFormatter<T>()
    {
        return FormatterCache<T>.Formatter;
    }

    private static object? _CreateFormatter(Type type)
    {
        var primitiveInterface = type.GetInterfaces()
            .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IPrimitive<>));

        if (primitiveInterface is null)
        {
            return null;
        }

        var underlyingType = primitiveInterface.GetGenericArguments()[0];
        var constructor = type.GetConstructor([underlyingType]);

        if (constructor is null)
        {
            return null;
        }

        var formatterType = typeof(PrimitiveFormatter<,>).MakeGenericType(type, underlyingType);

        return Activator.CreateInstance(formatterType, constructor);
    }

    private static class FormatterCache<T>
    {
        public static readonly IMessagePackFormatter<T>? Formatter = (IMessagePackFormatter<T>?)_CreateFormatter(
            typeof(T)
        );
    }

    private sealed class PrimitiveFormatter<TPrimitive, TValue>(ConstructorInfo constructor)
        : IMessagePackFormatter<TPrimitive>
        where TPrimitive : IPrimitive<TValue>
        where TValue : IEquatable<TValue>, IComparable, IComparable<TValue>
    {
        private readonly Func<TValue, TPrimitive> _create = _Compile(constructor);

        public void Serialize(ref MessagePackWriter writer, TPrimitive value, MessagePackSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNil();

                return;
            }

            options
                .Resolver.GetFormatterWithVerify<TValue>()
                .Serialize(ref writer, value.GetUnderlyingPrimitiveValue(), options);
        }

        public TPrimitive Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            if (reader.TryReadNil())
            {
                return default!;
            }

            var value = options.Resolver.GetFormatterWithVerify<TValue>().Deserialize(ref reader, options);

            return _create(value);
        }

        private static Func<TValue, TPrimitive> _Compile(ConstructorInfo constructor)
        {
            var value = Expression.Parameter(typeof(TValue), "value");

            return Expression.Lambda<Func<TValue, TPrimitive>>(Expression.New(constructor, value), value).Compile();
        }
    }
}
