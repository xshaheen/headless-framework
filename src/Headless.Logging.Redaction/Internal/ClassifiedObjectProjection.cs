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
/// unless its type alone shows it is safe.
/// </para>
/// <para>
/// Only a materialized collection is read. A sequence that is not one (a LINQ query, an iterator, an EF Core
/// <c>DbSet</c> or any other <see cref="IQueryable"/>) is never enumerated: enumerating it inside a log call could run a
/// database query or throw, so it prints as its type name. An exception prints as the provider prints it, through its
/// own <c>ToString()</c>, and is never walked: its <c>Data</c>, <c>TargetSite</c>, and members such as an EF Core
/// <c>DbUpdateException</c>'s entries lead into graphs a provider never prints. A getter or an enumerator that throws
/// prints a message naming the exception type and never fails the log call.
/// </para>
/// <para>
/// Some types are never walked because reading their members has effects: a task's <c>Result</c> blocks, a
/// <c>Lazy&lt;T&gt;</c>'s <c>Value</c> runs its factory, a cancellation token's <c>WaitHandle</c> allocates, and an EF Core
/// context, an entry, or a lazy-loading entity runs queries from its getters. They print as their type name. Each getter
/// runs at most once per log call, and one log call reads a bounded number of values and prints a bounded amount of text;
/// past either budget a value prints as its type name. Anything else that throws during the walk turns the whole value into
/// a placeholder naming the exception type.
/// </para>
/// </remarks>
internal sealed class ClassifiedObjectProjection(IRedactorProvider redactorProvider)
{
    private const int _MaxDepth = 8;

    // A sequence is read this far and no further, so one log call never walks an unbounded or huge collection.
    private const int _MaxElements = 100;

    private const string _Truncated = "...";

    // One log call looks at this many values at most, so a wide graph costs a bounded walk rather than one that grows
    // with the product of its collection sizes.
    private const int _MaxVisits = 4096;

    // One log call prints at most this much text; a value reached past it prints as its type name.
    private const int _MaxChars = 32 * 1024;

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

        // `{@Name}` asks for the members, which a type whose ToString prints only its name would otherwise lose.
        var expand = captured && shape.ExpandsWhenCaptured;

        if (shape.Kind == ShapeKind.Safe && !expand)
        {
            return false;
        }

        // The thread's walk is taken out of its slot while in use: a getter or redactor that logs projects with a
        // walk of its own instead of clearing this one halfway through.
        var walk = _cachedWalk ?? new Walk();
        _cachedWalk = null;

        try
        {
            if (!expand && !_Needs(value, 0, walk, inProjection: false))
            {
                return false;
            }

            _Project(value, captured, 0, walk);
            var builder = walk.Builder;

            // A single long member can overshoot the budget between checks; the text is cut there.
            projection = builder.Length > _MaxChars ? builder.ToString(0, _MaxChars) + _Truncated : builder.ToString();

            return true;
        }
#pragma warning disable CA1031 // A log call is the boundary: a value whose ToString, redactor, or metadata throws is replaced, never rethrown into the caller.
        catch (Exception ex) when (RedactionFailure.IsContained(ex))
#pragma warning restore CA1031
        {
            projection = RedactionFailure.Text(value, ex);

            return true;
        }
        finally
        {
            if (walk.TryReset())
            {
                _cachedWalk = walk;
            }
        }
    }

    [ThreadStatic]
    private static Walk? _cachedWalk;

    /// <summary>Whether <paramref name="value"/> reaches a classified member.</summary>
    /// <param name="value">The value to look at.</param>
    /// <param name="depth">How far below the logged value it sits.</param>
    /// <param name="walk">
    /// The log call's walk. Its <see cref="Walk.Seen"/> set holds the instances this check already looked at: one met
    /// again adds nothing, because the check stops at the first classified member or the depth cap, so a finished visit
    /// found neither, and an unfinished one is still being looked at higher up.
    /// </param>
    /// <param name="inProjection">
    /// The check runs inside a projection. An instance being projected around this value, reached again, does need
    /// projection here: printing this value raw would print that instance raw.
    /// </param>
    private static bool _Needs(object value, int depth, Walk walk, bool inProjection)
    {
        var shape = _GetShape(value.GetType());

        switch (shape.Kind)
        {
            case ShapeKind.Safe:
                return false;
            // A sequence the walk does not read may hold a classified element, so it fails closed.
            case ShapeKind.TypeClassified or ShapeKind.Opaque:
                return true;
        }

        // Below the cap nothing is looked at, so a value its type does not prove safe may hold a classified member.
        if (depth > _MaxDepth)
        {
            return true;
        }

        if (inProjection && walk.Projecting.Contains(value))
        {
            return true;
        }

        if (!walk.Seen.Add(value))
        {
            return false;
        }

        // Past the budget nothing more is looked at, so the value may hold a classified member.
        if (!walk.TrySpend())
        {
            return true;
        }

        if (shape.Kind == ShapeKind.Sequence)
        {
            // An enumerator that threw left elements unread, which may hold a classified member.
            if (walk.Reads.Read((IEnumerable)value) is not { } items)
            {
                return true;
            }

            // Elements past the cap are not read, so they may hold a classified member: project and truncate.
            if (items.Count > _MaxElements)
            {
                return true;
            }

            foreach (var item in items)
            {
                if (item is not null && _Needs(item, depth + 1, walk, inProjection))
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

            if (!member.StaticallySafe && _TryGet(member.Member, value, walk, out var nested) && nested is not null)
            {
                if (_Needs(nested, depth + 1, walk, inProjection))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Each level appends to the walk's one builder, so a nested object costs no string of its own.
    private void _Project(object value, bool captured, int depth, Walk walk)
    {
        var shape = _GetShape(value.GetType());

        if (shape.Kind == ShapeKind.TypeClassified)
        {
            walk.Builder.Append(_RedactScalar(redactorProvider.GetRedactor(shape.TypeClassifications!), value));

            return;
        }

        if (shape.Kind == ShapeKind.Opaque)
        {
            walk.Builder.Append(value.GetType().ToString());

            return;
        }

        walk.Projecting.Add(value);

        try
        {
            _ProjectComposite(value, shape, captured, depth, walk);
        }
        finally
        {
            walk.Projecting.Remove(value);
        }
    }

    private void _ProjectComposite(object value, TypeShape shape, bool captured, int depth, Walk walk)
    {
        var builder = walk.Builder;

        if (shape.Kind == ShapeKind.Sequence)
        {
            if (walk.Reads.Read((IEnumerable)value) is not { } items)
            {
                builder.Append(_EnumeratorThrew(walk.Reads, value));

                return;
            }

            var count = Math.Min(items.Count, _MaxElements);

            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                if (items[i] is { } item)
                {
                    _Format(item, captured, depth + 1, walk);
                }
                else
                {
                    builder.Append(LogPropertyRedactors.NullText);
                }
            }

            _AppendTruncation(builder, items.Count);

            return;
        }

        if (!captured && !shape.IsRecord)
        {
            builder.Append(value.GetType().ToString());

            return;
        }

        builder.Append(value.GetType().Name).Append(" { ");
        var first = true;

        foreach (var member in shape.Members)
        {
            if (!first)
            {
                builder.Append(", ");
            }

            first = false;
            builder.Append(member.Member.Name).Append(" = ");

            if (!_TryGet(member.Member, value, walk, out var memberValue))
            {
                builder.Append(memberValue);
                continue;
            }

            if (memberValue is null)
            {
                continue;
            }

            if (member.Classifications is { } classifications)
            {
                _AppendRedactedMember(memberValue, classifications, walk);
            }
            else
            {
                _Format(memberValue, captured, depth + 1, walk);
            }
        }

        builder.Append(" }");
    }

    private void _Format(object value, bool captured, int depth, Walk walk)
    {
        var builder = walk.Builder;

        // An instance met again inside its own projection is a cycle; its type name ends it.
        if (walk.Projecting.Contains(value))
        {
            builder.Append(value.GetType().ToString());

            return;
        }

        var shape = _GetShape(value.GetType());
        var kind = shape.Kind;

        // The check runs between appends, never inside another check, so it reuses the walk's one set of visits.
        if (!(captured && shape.ExpandsWhenCaptured) && (kind == ShapeKind.Safe || !_NeedsFresh(value, depth, walk)))
        {
            _AppendPlain(builder, value);

            return;
        }

        // Below the depth cap or past the budget a value is not walked: a classified type is still redacted whole, and
        // anything else prints as its type name, never through a ToString that could print a classified member.
        if ((depth > _MaxDepth || walk.Exhausted) && kind != ShapeKind.TypeClassified)
        {
            builder.Append(value.GetType().ToString());

            return;
        }

        _Project(value, captured, depth, walk);
    }

    private static bool _NeedsFresh(object value, int depth, Walk walk)
    {
        walk.Seen.Clear();

        return _Needs(value, depth, walk, inProjection: true);
    }

    // Logging renders a hole with the invariant culture; a span-formattable value is written straight into the builder.
    private static void _AppendPlain(StringBuilder builder, object value)
    {
        if (value is string text)
        {
            builder.Append(text);

            return;
        }

        builder.Append(CultureInfo.InvariantCulture, $"{value}");
    }

    private void _AppendRedactedMember(object value, DataClassificationSet classifications, Walk walk)
    {
        var redactor = redactorProvider.GetRedactor(classifications);
        var builder = walk.Builder;

        // A sequence the walk does not read is redacted as one value, from the text its type prints.
        if (value is not IEnumerable sequence || _IsSingleValue(value) || !_IsMaterialized(value.GetType()))
        {
            builder.Append(_RedactScalar(redactor, value));

            return;
        }

        if (walk.Reads.Read(sequence) is not { } items)
        {
            builder.Append(_EnumeratorThrew(walk.Reads, value));

            return;
        }

        var count = Math.Min(items.Count, _MaxElements);
        builder.Append('[');

        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(items[i] is { } item ? _RedactScalar(redactor, item) : LogPropertyRedactors.NullText);
        }

        _AppendTruncation(builder, items.Count);
        builder.Append(']');
    }

    // At most the element cap is printed, then a marker when the sequence held more.
    private static void _AppendTruncation(StringBuilder builder, int read)
    {
        if (read > _MaxElements)
        {
            builder.Append(", ").Append(_Truncated);
        }
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

    private static string _EnumeratorThrew(SequenceReads reads, object sequence) =>
        "The enumerator threw an exception: " + reads.Failure(sequence);

    // A getter runs once per log call: the check and the projection see the same value, and a re-check of a subtree
    // does not run its getters again.
    private static bool _TryGet(MemberInfo member, object instance, Walk walk, out object? value)
    {
        var getters = walk.Getters;
        var key = new GetterKey(instance, member);

        if (getters.TryGetValue(key, out var read))
        {
            value = read.Value;

            return read.Succeeded;
        }

        var succeeded = _Invoke(member, instance, out value);
        getters[key] = new GetterRead(succeeded, value);

        return succeeded;
    }

    // A throwing getter becomes a value naming the exception type, so one bad member does not fail the log call.
    private static bool _Invoke(MemberInfo member, object instance, out object? value)
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

        if (WalkLeaves.IsLeaf(type))
        {
            return TypeShape.Opaque;
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
        // A provider prints an exception through its ToString: message, stack, and inner exceptions, never its members.
        if (typeof(Exception).IsAssignableFrom(type))
        {
            return TypeShape.Safe;
        }

        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            var element = _ElementType(type);

            // A sequence of statically safe elements is safe; otherwise each element of a collection is checked by its
            // value, and any other sequence is not read at all.
            if (element is not null && _IsStaticallySafeDeclared(element, inProgress))
            {
                return TypeShape.Safe;
            }

            return _IsMaterialized(type)
                ? new TypeShape(ShapeKind.Sequence, TypeClassifications: null, [], IsRecord: false)
                : TypeShape.Opaque;
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

        // Base members first, as a record prints them. Reflection keeps no order between a type's fields and its
        // properties, so a type that interleaves them prints its properties first.
        return new TypeShape(
            allSafe ? ShapeKind.Safe : ShapeKind.Members,
            TypeClassifications: null,
            [.. members.OrderBy(m => _InheritanceDepth(m.Member.DeclaringType))],
            _IsRecord(type)
        )
        {
            ExpandsWhenCaptured = members.Count > 0 && _PrintsOnlyItsName(type),
        };
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
        || _IsReflectionMetadata(type)
        // Its Directory, Parent, and Root chain reaches the depth cap, and reading its members stats the file on every
        // log call; the provider prints the path it was created with.
        || typeof(FileSystemInfo).IsAssignableFrom(type)
        // It holds no data of its own, and reading its WaitHandle allocates an event the token then keeps.
        || type == typeof(CancellationToken)
        || (Nullable.GetUnderlyingType(type) is { } underlying && _IsScalar(underlying));

    // A logged Type, MethodInfo, or Assembly is a runtime subclass (RuntimeType, RuntimeMethodInfo, RuntimeAssembly), so
    // an exact type test never matches it. Walked as a composite, its member graph reaches the depth cap and prints the
    // runtime class name instead of the name the provider would print.
    private static bool _IsReflectionMetadata(Type type) =>
        typeof(MemberInfo).IsAssignableFrom(type)
        || typeof(ParameterInfo).IsAssignableFrom(type)
        || typeof(Assembly).IsAssignableFrom(type)
        || typeof(Module).IsAssignableFrom(type);

    /// <summary>
    /// Whether enumerating a value of <paramref name="type"/> only reads elements it already holds. A query, an iterator,
    /// or an EF Core <c>DbSet</c> computes them on enumeration, which can run a database query or throw.
    /// </summary>
    private static bool _IsMaterialized(Type type) => _Materialized.GetOrAdd(type, static t => _ComputeMaterialized(t));

    private static readonly ConcurrentDictionary<Type, bool> _Materialized = new();

    private static bool _ComputeMaterialized(Type type)
    {
        if (typeof(IQueryable).IsAssignableFrom(type))
        {
            return false;
        }

        if (type.IsArray || typeof(ICollection).IsAssignableFrom(type))
        {
            return true;
        }

        foreach (var contract in type.GetInterfaces())
        {
            if (
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() is var definition
                && (definition == typeof(ICollection<>) || definition == typeof(IReadOnlyCollection<>))
            )
            {
                return true;
            }
        }

        return false;
    }

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

    // The ToString of object and ValueType prints the type name; any override is the type's own chosen text.
    private static bool _PrintsOnlyItsName(Type type) =>
        type.GetMethod(nameof(ToString), Type.EmptyTypes)?.DeclaringType is { } declaring
        && (declaring == typeof(object) || declaring == typeof(ValueType));

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

        /// <summary>A materialized collection, read once per log call.</summary>
        Sequence,

        /// <summary>
        /// A sequence that is not a collection, or a type whose members have effects; it is never read and prints as its
        /// type name.
        /// </summary>
        Opaque,
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

        public static readonly TypeShape Opaque = new(ShapeKind.Opaque, TypeClassifications: null, [], IsRecord: false);

        /// <summary>
        /// A value captured as <c>{@Name}</c> prints its members: the type has some, and its ToString would print
        /// only its name.
        /// </summary>
        public bool ExpandsWhenCaptured { get; init; }
    }

    // The elements a collection gave, or the name of the exception its enumerator threw.
    private readonly record struct SequenceRead(List<object?>? Items, string? Failure);

    // One getter of one instance. The instance is compared by reference: a record's own Equals walks its members.
    private readonly struct GetterKey(object instance, MemberInfo member) : IEquatable<GetterKey>
    {
        private readonly object _instance = instance;
        private readonly MemberInfo _member = member;

        public bool Equals(GetterKey other) => ReferenceEquals(_instance, other._instance) && _member == other._member;

        public override bool Equals(object? obj) => obj is GetterKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(_instance), _member);
    }

    private readonly record struct GetterRead(bool Succeeded, object? Value);

    // A public property or a public instance field.
    private sealed record MemberShape(MemberInfo Member, DataClassificationSet? Classifications, bool StaticallySafe);

    // The elements each collection gave during one log call, so detection and rendering see the same elements even
    // when another thread changes the collection between them, and each collection is enumerated once. A logged
    // sequence whose element type is safe is passed through without being read.
    private sealed class SequenceReads
    {
        private Dictionary<object, SequenceRead>? _reads;

        /// <summary>The elements read, or <see langword="null"/> when the enumerator threw.</summary>
        public List<object?>? Read(IEnumerable sequence)
        {
            _reads ??= new Dictionary<object, SequenceRead>(ReferenceEqualityComparer.Instance);

            if (_reads.TryGetValue(sequence, out var read))
            {
                return read.Items;
            }

            read = _ReadOnce(sequence);
            _reads[sequence] = read;

            return read.Items;
        }

        public string? Failure(object sequence) =>
            _reads is not null && _reads.TryGetValue(sequence, out var read) ? read.Failure : null;

        private static SequenceRead _ReadOnce(IEnumerable sequence)
        {
            List<object?> items = [];

            try
            {
                foreach (var item in sequence)
                {
                    items.Add(item);

                    // One element past the cap is enough to know the sequence was cut.
                    if (items.Count > _MaxElements)
                    {
                        break;
                    }
                }
            }
#pragma warning disable CA1031 // A log call is the boundary: whatever a collection's enumerator throws, the entry still logs with a placeholder.
            catch (Exception ex) when (RedactionFailure.IsContained(ex))
#pragma warning restore CA1031
            {
                return new SequenceRead(Items: null, ex.GetType().Name);
            }

            return new SequenceRead(items, Failure: null);
        }

        public int Capacity => _reads?.EnsureCapacity(0) ?? 0;

        public void Clear() => _reads?.Clear();
    }

    /// <summary>
    /// The state one projection works in, kept per thread and cleared after each log call, so a steady stream of
    /// classified objects allocates only the text it prints.
    /// </summary>
    private sealed class Walk
    {
        // A walk that grew past these after one large object is dropped rather than kept for the thread's lifetime.
        private const int _MaxKeptInstances = 256;
        private const int _MaxKeptChars = 4096;

        // Reference equality: a record's own Equals and GetHashCode walk its members, and on a cyclic graph never
        // return.
        public HashSet<object> Seen { get; } = new(ReferenceEqualityComparer.Instance);

        public HashSet<object> Projecting { get; } = new(ReferenceEqualityComparer.Instance);

        public SequenceReads Reads { get; } = new();

        public Dictionary<GetterKey, GetterRead> Getters { get; } = [];

        public StringBuilder Builder { get; } = new();

        private int _visits;

        /// <summary>Whether the log call spent its budget of values or text.</summary>
        public bool Exhausted => _visits >= _MaxVisits || Builder.Length >= _MaxChars;

        /// <summary>Counts one more value looked at; returns whether the budget allowed it.</summary>
        public bool TrySpend()
        {
            if (Exhausted)
            {
                return false;
            }

            _visits++;

            return true;
        }

        /// <summary>Clears every reference the walk held; returns whether it is small enough to keep.</summary>
        public bool TryReset()
        {
            Seen.Clear();
            Projecting.Clear();
            Reads.Clear();
            Getters.Clear();
            Builder.Clear();
            _visits = 0;

            return Seen.EnsureCapacity(0) <= _MaxKeptInstances
                && Projecting.EnsureCapacity(0) <= _MaxKeptInstances
                && Reads.Capacity <= _MaxKeptInstances
                && Getters.EnsureCapacity(0) <= _MaxKeptInstances
                && Builder.Capacity <= _MaxKeptChars;
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
