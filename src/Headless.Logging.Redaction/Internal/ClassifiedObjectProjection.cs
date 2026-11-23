// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;

namespace Headless.Logging.Internal;

/// <summary>
/// Replaces a logged object that carries a classified member, directly or through a nested value, with a redacted text
/// form before any provider prints it.
/// </summary>
/// <remarks>
/// <para>
/// Under Microsoft.Extensions.Logging a provider prints an object hole, <c>{@Value}</c> included, through the object's
/// <c>ToString()</c>, so a record's <c>[PrivateData]</c> member would reach the console and the exporter raw.
/// </para>
/// <para>
/// The projection never shows more than the provider would have printed. A record with the compiler's <c>ToString()</c>,
/// or a value captured as <c>{@Name}</c>, becomes <c>Type { Member = value, ... }</c> with classified members redacted.
/// Any other type becomes its type name, which is what the default <c>ToString()</c> prints, so an override that might
/// print a member is not trusted.
/// </para>
/// <para>
/// A type whose members are all scalar or sealed, and unclassified all the way down, is remembered as safe and costs one
/// lookup afterwards. A member declared as <see cref="object"/>, an interface, or a type that can be derived from is checked by
/// the value it holds, because that value can be a classified type.
/// </para>
/// </remarks>
internal sealed class ClassifiedObjectProjection(IRedactorProvider redactorProvider)
{
    private const int _MaxDepth = 8;

    // Reflection metadata does not depend on the redactor provider, so it is shared across instances.
    private static readonly ConcurrentDictionary<Type, TypeShape> _Shapes = new();

    /// <summary>
    /// Whether every value a slot declared as <paramref name="type"/> can hold is safe, decided once per type. A slot
    /// declared as <see cref="object"/> or a type that can be derived from is not: its value decides.
    /// </summary>
    public static bool IsStaticallySafe(Type type) =>
        _DeclaredSafe.GetOrAdd(type, static t => _IsStaticallySafeDeclared(t, []));

    private static readonly ConcurrentDictionary<Type, bool> _DeclaredSafe = new();

    /// <summary>
    /// Returns the projected text when <paramref name="value"/> needs one; otherwise <see langword="false"/> and no
    /// allocation.
    /// </summary>
    /// <param name="value">The logged value.</param>
    /// <param name="captured">The hole was written <c>{@Name}</c>, so members may be shown.</param>
    /// <param name="projection">The redacted text form.</param>
    public bool TryProject(object? value, bool captured, out string projection)
    {
        projection = null!;

        if (value is null || !_Needs(value, 0))
        {
            return false;
        }

        projection = _Project(value, captured, 0);

        return true;
    }

    private static bool _Needs(object value, int depth)
    {
        if (depth > _MaxDepth)
        {
            return false;
        }

        var shape = _GetShape(value.GetType());

        switch (shape.Kind)
        {
            case ShapeKind.Safe:
                return false;
            case ShapeKind.TypeClassified:
                return true;
            case ShapeKind.Sequence:
                foreach (var item in (IEnumerable)value)
                {
                    if (item is not null && _Needs(item, depth + 1))
                    {
                        return true;
                    }
                }

                return false;
        }

        foreach (var member in shape.Members)
        {
            if (member.Classifications is not null)
            {
                return true;
            }

            if (!member.StaticallySafe && _TryGet(member.Property, value, out var nested) && nested is not null)
            {
                if (_Needs(nested, depth + 1))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private string _Project(object value, bool captured, int depth)
    {
        var shape = _GetShape(value.GetType());

        if (shape.Kind == ShapeKind.TypeClassified)
        {
            return _RedactScalar(redactorProvider.GetRedactor(shape.TypeClassifications!), value);
        }

        if (shape.Kind == ShapeKind.Sequence)
        {
            var items = new List<string>();

            foreach (var item in (IEnumerable)value)
            {
                items.Add(item is null ? "(null)" : _Format(item, captured, depth + 1));
            }

            return string.Join(", ", items);
        }

        if (!captured && !shape.IsRecord)
        {
            return value.GetType().ToString();
        }

        var builder = new StringBuilder(value.GetType().Name).Append(" { ");
        var first = true;

        foreach (var member in shape.Members)
        {
            if (!first)
            {
                builder.Append(", ");
            }

            first = false;
            builder.Append(member.Property.Name).Append(" = ");

            if (!_TryGet(member.Property, value, out var memberValue))
            {
                builder.Append(memberValue);
                continue;
            }

            if (memberValue is null)
            {
                continue;
            }

            builder.Append(
                member.Classifications is { } classifications
                    ? _RedactMember(memberValue, classifications)
                    : _Format(memberValue, captured, depth + 1)
            );
        }

        return builder.Append(" }").ToString();
    }

    private string _Format(object value, bool captured, int depth)
    {
        if (depth <= _MaxDepth && _Needs(value, depth))
        {
            return _Project(value, captured, depth);
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private string _RedactMember(object value, DataClassificationSet classifications)
    {
        var redactor = redactorProvider.GetRedactor(classifications);

        if (value is IEnumerable sequence && !_IsSingleValue(value))
        {
            var items = new List<string>();

            foreach (var item in sequence)
            {
                items.Add(item is null ? "(null)" : _RedactScalar(redactor, item));
            }

            return "[" + string.Join(", ", items) + "]";
        }

        return _RedactScalar(redactor, value);
    }

    // A string or buffer is one value; redacting it element by element would give each character its own token.
    private static bool _IsSingleValue(object value) => value is string or char[] or byte[];

    private static string _RedactScalar(Redactor redactor, object value) =>
        value switch
        {
            char[] chars => redactor.Redact(new string(chars)),
            byte[] bytes => redactor.Redact(Convert.ToBase64String(bytes)),
            // Invariant culture keeps a pseudonym the same on every host.
            _ => redactor.Redact(value, format: null, CultureInfo.InvariantCulture),
        };

    // A throwing getter becomes a value naming the exception type, so one bad member does not fail the log call.
    private static bool _TryGet(PropertyInfo property, object instance, out object? value)
    {
        try
        {
            value = property.GetValue(instance);

            return true;
        }
        catch (TargetInvocationException ex)
        {
            value = "The property accessor threw an exception: " + ex.InnerException?.GetType().Name;

            return false;
        }
        catch (NotSupportedException)
        {
            value = "Accessing this property is not supported via Reflection API";

            return false;
        }
    }

    private static TypeShape _GetShape(Type type) => _Shapes.GetOrAdd(type, static t => _CreateShape(t, []));

    private static TypeShape _CreateShape(Type type, HashSet<Type> inProgress)
    {
        if (_Shapes.TryGetValue(type, out var known))
        {
            return known;
        }

        if (_IsScalar(type))
        {
            return TypeShape.Safe;
        }

        if (_ReadClassifications(type) is { } typeClassifications)
        {
            return new TypeShape(ShapeKind.TypeClassified, typeClassifications, [], IsRecord: false);
        }

        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            var element = _ElementType(type);

            // A sequence of statically safe elements is safe; otherwise each element is checked by its value.
            return element is not null && _IsStaticallySafeDeclared(element, inProgress)
                ? TypeShape.Safe
                : new TypeShape(ShapeKind.Sequence, TypeClassifications: null, [], IsRecord: false);
        }

        inProgress.Add(type);
        var members = new List<MemberShape>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var allSafe = true;

        var properties = type.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy
        );

        // Most-derived first so a property hidden with `new` appears once, as the derived declaration.
        foreach (var property in properties.OrderByDescending(p => _InheritanceDepth(p.DeclaringType)))
        {
            if (
                property.GetMethod is not { IsPublic: true }
                || property.GetIndexParameters().Length != 0
                || !seen.Add(property.Name)
                || _IsRecordContract(property)
            )
            {
                continue;
            }

            var classifications = _ReadClassifications(property);
            var memberSafe = classifications is null && _IsStaticallySafeDeclared(property.PropertyType, inProgress);
            allSafe &= memberSafe;
            members.Add(new MemberShape(property, classifications, memberSafe));
        }

        inProgress.Remove(type);

        if (allSafe)
        {
            return TypeShape.Safe;
        }

        return new TypeShape(
            ShapeKind.Members,
            TypeClassifications: null,
            [.. members.OrderBy(m => _InheritanceDepth(m.Property.DeclaringType))],
            _IsRecord(type)
        );
    }

    // A declared member type is safe without looking at values only when no value it can hold carries a classification:
    // scalars, and sealed types (value types included) whose own members are safe the same way.
    private static bool _IsStaticallySafeDeclared(Type declared, HashSet<Type> inProgress)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;

        if (_IsScalar(type))
        {
            return true;
        }

        if (type.IsArray)
        {
            return _IsStaticallySafeDeclared(type.GetElementType()!, inProgress);
        }

        if (!type.IsValueType && !type.IsSealed)
        {
            // A generic collection of safe elements is safe whatever the collection class.
            return typeof(IEnumerable).IsAssignableFrom(type)
                && _ElementType(type) is { } element
                && _IsStaticallySafeDeclared(element, inProgress);
        }

        // A type already being shaped higher up refers to itself; its values are checked at log time.
        if (inProgress.Contains(type))
        {
            return false;
        }

        var shape = _Shapes.TryGetValue(type, out var known) ? known : _CreateShape(type, inProgress);
        _Shapes.TryAdd(type, shape);

        return shape.Kind == ShapeKind.Safe;
    }

    private static bool _IsScalar(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(decimal)
        || type == typeof(DateTime)
        || type == typeof(DateTimeOffset)
        || type == typeof(DateOnly)
        || type == typeof(TimeOnly)
        || type == typeof(TimeSpan)
        || type == typeof(Guid)
        || type == typeof(Uri)
        || type == typeof(Version)
        || type == typeof(byte[])
        || type == typeof(char[])
        || type == typeof(Type)
        || (Nullable.GetUnderlyingType(type) is { } underlying && _IsScalar(underlying));

    private static Type? _ElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        return type.GetInterfaces()
            .Append(type)
            .FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    // The compiler adds EqualityContract to records; it is not data.
    private static bool _IsRecordContract(PropertyInfo property) =>
        string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal)
        && property.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    // A record whose ToString is the compiler's prints its members, so showing them hides nothing it did not show.
    private static bool _IsRecord(Type type) =>
        type.GetMethod("<Clone>$") is not null
        && type.GetMethod(nameof(ToString), Type.EmptyTypes) is { } toString
        && toString.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static DataClassificationSet? _ReadClassifications(MemberInfo member)
    {
        List<DataClassification>? classifications = null;

        foreach (
            var attribute in Attribute.GetCustomAttributes(member, typeof(DataClassificationAttribute), inherit: true)
        )
        {
            var classification = ((DataClassificationAttribute)attribute).Classification;

            if (classification != DataClassification.None)
            {
                (classifications ??= []).Add(classification);
            }
        }

        return classifications is null ? null : new DataClassificationSet(classifications);
    }

    private static int _InheritanceDepth(Type? type)
    {
        var depth = 0;

        for (var current = type; current is not null; current = current.BaseType)
        {
            depth++;
        }

        return depth;
    }

    private enum ShapeKind
    {
        Safe,
        TypeClassified,
        Sequence,
        Members,
    }

    private sealed record TypeShape(
        ShapeKind Kind,
        DataClassificationSet? TypeClassifications,
        MemberShape[] Members,
        bool IsRecord
    )
    {
        public static readonly TypeShape Safe = new(ShapeKind.Safe, TypeClassifications: null, [], IsRecord: false);
    }

    private sealed record MemberShape(
        PropertyInfo Property,
        DataClassificationSet? Classifications,
        bool StaticallySafe
    );
}

/// <summary>
/// Whether a message state of type <typeparamref name="TState"/> can carry an object at all, decided once per type, so
/// a state built from scalars only is never boxed to look.
/// </summary>
internal static class LogStateShape<TState>
{
    public static readonly bool MayCarryObjects = _Compute(typeof(TState));

    private static bool _Compute(Type type)
    {
        // A reference-type state is read without boxing, so it is always looked at.
        if (!type.IsValueType)
        {
            return true;
        }

        // Microsoft's LoggerMessage.Define states are generic structs over the message's argument types.
        return !type.IsGenericType || !type.GetGenericArguments().All(ClassifiedObjectProjection.IsStaticallySafe);
    }
}
