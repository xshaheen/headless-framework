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
/// <para>
/// The walk stops below a fixed depth and fails closed there: a value it does not reach is printed as its type name,
/// unless its type alone shows it is safe. A sequence is read once per log call, so a lazy query runs once and a
/// one-shot sequence is not enumerated twice.
/// </para>
/// </remarks>
internal sealed class ClassifiedObjectProjection(IRedactorProvider redactorProvider)
{
    private const int _MaxDepth = 8;

    // A sequence is read this far and no further, so one log call never walks an unbounded or huge collection.
    private const int _MaxElements = 100;

    private const string _Truncated = "...";

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

        // Most logged values are strings, numbers, dates, and enums: a type-code check rules them out without the
        // shape lookup, which every entry would otherwise pay once per value.
        if (value is null || Type.GetTypeCode(value.GetType()) != TypeCode.Object)
        {
            return false;
        }

        // A safe type is decided before any walk state is allocated, so the common case stays allocation-free.
        var shape = _GetShape(value.GetType());

        if (shape.Kind == ShapeKind.Safe)
        {
            return false;
        }

        var reads = new SequenceReads();

        // A sequence that is not a collection was enumerated by the check: the provider printing it again would run a
        // lazy query twice, and a one-shot sequence would give it nothing or throw. Print the elements already read.
        if (
            !_Needs(value, 0, _NewInstanceSet(), projecting: null, reads)
            && (shape.Kind != ShapeKind.Sequence || value is ICollection)
        )
        {
            return false;
        }

        projection = _Project(value, captured, 0, _NewInstanceSet(), reads);

        return true;
    }

    // Reference equality: a record's own Equals and GetHashCode walk its members, and on a cyclic graph never return.
    private static HashSet<object> _NewInstanceSet() => new(ReferenceEqualityComparer.Instance);

    /// <summary>Whether <paramref name="value"/> reaches a classified member.</summary>
    /// <param name="value">The value to look at.</param>
    /// <param name="depth">How far below the logged value it sits.</param>
    /// <param name="seen">
    /// The instances this walk already looked at. One met again adds nothing: the walk stops at the first classified
    /// member or the depth cap, so a finished visit found neither, and an unfinished one is still being looked at
    /// higher up.
    /// </param>
    /// <param name="projecting">
    /// The instances being projected around this value. One of them reached again does need projection here: printing
    /// this value raw would print that instance raw.
    /// </param>
    /// <param name="reads">The elements of each sequence this log call already read.</param>
    private static bool _Needs(
        object value,
        int depth,
        HashSet<object> seen,
        HashSet<object>? projecting,
        SequenceReads reads
    )
    {
        var shape = _GetShape(value.GetType());

        switch (shape.Kind)
        {
            case ShapeKind.Safe:
                return false;
            case ShapeKind.TypeClassified:
                return true;
        }

        // Below the cap nothing is looked at, so a value its type does not prove safe may hold a classified member.
        if (depth > _MaxDepth)
        {
            return true;
        }

        if (projecting?.Contains(value) == true)
        {
            return true;
        }

        if (!seen.Add(value))
        {
            return false;
        }

        if (shape.Kind == ShapeKind.Sequence)
        {
            var items = reads.Read((IEnumerable)value);

            // Elements past the cap are not read, so they may hold a classified member: project and truncate.
            if (items.Count > _MaxElements)
            {
                return true;
            }

            foreach (var item in items)
            {
                if (item is not null && _Needs(item, depth + 1, seen, projecting, reads))
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

            if (!member.StaticallySafe && _TryGet(member.Member, value, out var nested) && nested is not null)
            {
                if (_Needs(nested, depth + 1, seen, projecting, reads))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private string _Project(object value, bool captured, int depth, HashSet<object> projecting, SequenceReads reads)
    {
        var shape = _GetShape(value.GetType());

        if (shape.Kind == ShapeKind.TypeClassified)
        {
            return _RedactScalar(redactorProvider.GetRedactor(shape.TypeClassifications!), value);
        }

        projecting.Add(value);

        try
        {
            return _ProjectComposite(value, shape, captured, depth, projecting, reads);
        }
        finally
        {
            projecting.Remove(value);
        }
    }

    private string _ProjectComposite(
        object value,
        TypeShape shape,
        bool captured,
        int depth,
        HashSet<object> projecting,
        SequenceReads reads
    )
    {
        if (shape.Kind == ShapeKind.Sequence)
        {
            var items = _Render(
                reads.Read((IEnumerable)value),
                item => _Format(item, captured, depth + 1, projecting, reads)
            );

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
            builder.Append(member.Member.Name).Append(" = ");

            if (!_TryGet(member.Member, value, out var memberValue))
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
                    ? _RedactMember(memberValue, classifications, reads)
                    : _Format(memberValue, captured, depth + 1, projecting, reads)
            );
        }

        return builder.Append(" }").ToString();
    }

    private string _Format(object value, bool captured, int depth, HashSet<object> projecting, SequenceReads reads)
    {
        // An instance met again inside its own projection is a cycle; its type name ends it.
        if (projecting.Contains(value))
        {
            return value.GetType().ToString();
        }

        if (!_Needs(value, depth, _NewInstanceSet(), projecting, reads))
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        // Below the depth cap a value is not walked: a classified type is still redacted whole, and anything else
        // prints as its type name, never through a ToString that could print a classified member.
        return depth > _MaxDepth && _GetShape(value.GetType()).Kind != ShapeKind.TypeClassified
            ? value.GetType().ToString()
            : _Project(value, captured, depth, projecting, reads);
    }

    private string _RedactMember(object value, DataClassificationSet classifications, SequenceReads reads)
    {
        var redactor = redactorProvider.GetRedactor(classifications);

        if (value is IEnumerable sequence && !_IsSingleValue(value))
        {
            return "[" + string.Join(", ", _Render(reads.Read(sequence), item => _RedactScalar(redactor, item))) + "]";
        }

        return _RedactScalar(redactor, value);
    }

    // At most the element cap, then a marker when the sequence held more.
    private static List<string> _Render(List<object?> items, Func<object, string> format)
    {
        var count = Math.Min(items.Count, _MaxElements);
        var rendered = new List<string>(count + 1);

        for (var i = 0; i < count; i++)
        {
            rendered.Add(items[i] is { } item ? format(item) : "(null)");
        }

        if (items.Count > _MaxElements)
        {
            rendered.Add(_Truncated);
        }

        return rendered;
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
    private static bool _TryGet(MemberInfo member, object instance, out object? value)
    {
        try
        {
            value = member is FieldInfo field ? field.GetValue(instance) : ((PropertyInfo)member).GetValue(instance);

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

        // Marked before any edge is followed, so a type reached again through its own element or member type ends the
        // walk instead of recursing; only the call that marked it unmarks it.
        var marked = inProgress.Add(type);

        try
        {
            return _CreateComposite(type, inProgress);
        }
        finally
        {
            if (marked)
            {
                inProgress.Remove(type);
            }
        }
    }

    private static TypeShape _CreateComposite(Type type, HashSet<Type> inProgress)
    {
        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            var element = _ElementType(type);

            // A sequence of statically safe elements is safe; otherwise each element is checked by its value.
            return element is not null && _IsStaticallySafeDeclared(element, inProgress)
                ? TypeShape.Safe
                : new TypeShape(ShapeKind.Sequence, TypeClassifications: null, [], IsRecord: false);
        }

        var members = new List<MemberShape>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var allSafe = true;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
        IEnumerable<MemberInfo> candidates = [.. type.GetProperties(flags), .. type.GetFields(flags)];

        // Most-derived first so a member hidden with `new` appears once, as the derived declaration. The sort is
        // stable, so within one type properties come before fields.
        foreach (var member in candidates.OrderByDescending(m => _InheritanceDepth(m.DeclaringType)))
        {
            if (!_IsDataMember(member) || !seen.Add(member.Name))
            {
                continue;
            }

            var classifications = member is PropertyInfo property
                ? _ReadPropertyClassifications(property, type)
                : _ReadClassifications(member);

            var memberSafe = classifications is null && _IsStaticallySafeDeclared(_MemberType(member), inProgress);
            allSafe &= memberSafe;
            members.Add(new MemberShape(member, classifications, memberSafe));
        }

        if (allSafe)
        {
            return TypeShape.Safe;
        }

        // Base members first, as a record prints them. Reflection keeps no order between a type's fields and its
        // properties, so a type that interleaves them prints its properties first.
        return new TypeShape(
            ShapeKind.Members,
            TypeClassifications: null,
            [.. members.OrderBy(m => _InheritanceDepth(m.Member.DeclaringType))],
            _IsRecord(type)
        );
    }

    private static bool _IsDataMember(MemberInfo member)
    {
        if (member is PropertyInfo property)
        {
            return property.GetMethod is { IsPublic: true }
                && property.GetIndexParameters().Length == 0
                && !_IsRecordContract(property);
        }

        // Backing fields are never public, but a compiler-generated field is not data the type declared.
        return !member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) && !member.Name.StartsWith('<');
    }

    private static Type _MemberType(MemberInfo member) =>
        member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;

    // A declared member type is safe without looking at values only when no value it can hold carries a classification:
    // scalars, and sealed types (value types included) whose own members are safe the same way.
    private static bool _IsStaticallySafeDeclared(Type declared, HashSet<Type> inProgress)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;

        if (_IsScalar(type))
        {
            return true;
        }

        // A type already being shaped higher up refers to itself; its values are checked at log time.
        if (inProgress.Contains(type))
        {
            return false;
        }

        if (type.IsArray || (!type.IsValueType && !type.IsSealed))
        {
            // A generic collection of safe elements is safe whatever the collection class.
            if ((!type.IsArray && !typeof(IEnumerable).IsAssignableFrom(type)) || _ElementType(type) is not { } element)
            {
                return false;
            }

            inProgress.Add(type);

            try
            {
                return _IsStaticallySafeDeclared(element, inProgress);
            }
            finally
            {
                inProgress.Remove(type);
            }
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
        _Collect(
            Attribute.GetCustomAttributes(member, typeof(DataClassificationAttribute), inherit: true),
            ref classifications
        );

        return classifications is null ? null : new DataClassificationSet(classifications);
    }

    // An untargeted attribute on a positional record or primary-constructor parameter lands on the parameter only, yet
    // the same-named property prints that value, so the parameter's classification is the property's too.
    private static DataClassificationSet? _ReadPropertyClassifications(PropertyInfo property, Type shapedType)
    {
        List<DataClassification>? classifications = null;
        _Collect(
            Attribute.GetCustomAttributes(property, typeof(DataClassificationAttribute), inherit: true),
            ref classifications
        );
        _CollectParameters(shapedType, property, ref classifications);

        if (property.DeclaringType is { } declaring && declaring != shapedType)
        {
            _CollectParameters(declaring, property, ref classifications);
        }

        return classifications is null ? null : new DataClassificationSet(classifications);
    }

    private static void _CollectParameters(
        Type type,
        PropertyInfo property,
        ref List<DataClassification>? classifications
    )
    {
        // An abstract record's primary constructor is protected, so non-public constructors count too.
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var constructor in type.GetConstructors(flags))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                if (
                    parameter.ParameterType == property.PropertyType
                    && string.Equals(parameter.Name, property.Name, StringComparison.Ordinal)
                )
                {
                    _Collect(
                        Attribute.GetCustomAttributes(parameter, typeof(DataClassificationAttribute), inherit: true),
                        ref classifications
                    );
                }
            }
        }
    }

    private static void _Collect(Attribute[] attributes, ref List<DataClassification>? classifications)
    {
        foreach (var attribute in attributes)
        {
            var classification = ((DataClassificationAttribute)attribute).Classification;

            if (classification != DataClassification.None)
            {
                (classifications ??= []).Add(classification);
            }
        }
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

    // A public property or a public instance field.
    private sealed record MemberShape(MemberInfo Member, DataClassificationSet? Classifications, bool StaticallySafe);

    // The elements each sequence gave during one log call, so detection and rendering read the same elements and a
    // lazy query or a one-shot sequence is enumerated once. A logged sequence whose element type is safe is passed
    // through without being read.
    private sealed class SequenceReads
    {
        private Dictionary<object, List<object?>>? _reads;

        public List<object?> Read(IEnumerable sequence)
        {
            _reads ??= new Dictionary<object, List<object?>>(ReferenceEqualityComparer.Instance);

            if (_reads.TryGetValue(sequence, out var items))
            {
                return items;
            }

            items = [];

            foreach (var item in sequence)
            {
                items.Add(item);

                // One element past the cap is enough to know the sequence was cut.
                if (items.Count > _MaxElements)
                {
                    break;
                }
            }

            _reads[sequence] = items;

            return items;
        }
    }
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
