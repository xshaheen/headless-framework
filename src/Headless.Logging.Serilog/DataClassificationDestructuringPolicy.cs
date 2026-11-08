// Copyright (c) Mahmoud Shaheen. All rights reserved.
// Adapted from Serilog.Redaction (https://github.com/zyofeng/serilog-redaction), Apache License 2.0; modified.
// See THIRD-PARTY-NOTICES.md in this package.

using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Headless.Checks;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace Headless.Logging;

/// <summary>
/// A Serilog destructuring policy that redacts members marked with a <see cref="DataClassificationAttribute"/>
/// through the <see cref="IRedactorProvider"/> that Microsoft's compliance redaction configures, so one set of
/// classifications drives both Microsoft logging and Serilog.
/// </summary>
/// <remarks>
/// <para>
/// The policy runs when Serilog destructures a value: a <c>{@Value}</c> template hole, a destructured
/// <c>LogContext</c> property, or a nested object inside one. A plain <c>{Value}</c> hole renders the value's
/// <see cref="object.ToString"/> and never reaches a destructuring policy, so a type that carries a secret still
/// needs a <see cref="object.ToString"/> that hides it.
/// </para>
/// <para>
/// A type is handled only when it, or one of its public readable properties, carries a classification other than
/// <see cref="DataClassification.None"/>; every other type falls through to the next policy and to Serilog's
/// default destructuring. For a handled type:
/// </para>
/// <list type="bullet">
///   <item>A type-level classification redacts the whole value to one string.</item>
///   <item>
///   A classified property keeps its name and holds the redactor's output. An erasing redactor yields an empty
///   string, so the log still shows the property was set. A <see langword="null"/> value stays
///   <see langword="null"/>. Several classifications on one property are redacted as one combined set.
///   </item>
///   <item>
///   A classified collection (any <see cref="IEnumerable"/> except <see cref="string"/>) is redacted element by
///   element, so the log keeps the count and, with a pseudonymizing redactor, which elements repeat.
///   </item>
///   <item>
///   Unclassified properties are destructured by Serilog as usual, which applies this policy again to nested
///   objects, honoring Serilog's depth, string-length, and collection-length limits.
///   </item>
/// </list>
/// <para>
/// Properties are read the way Serilog's own destructurer reads them: public instance properties with a public
/// getter, inherited ones included, the most derived declaration winning, indexers skipped. A getter that throws
/// produces a message naming the exception type instead of failing the event.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class DataClassificationDestructuringPolicy(IRedactorProvider redactorProvider) : IDestructuringPolicy
{
    // Reflection metadata is provider-independent, so it is shared across instances; redactors are resolved per
    // instance because each instance can carry a different provider.
    private static readonly ConcurrentDictionary<Type, TypeShape?> _Shapes = new();

    private readonly IRedactorProvider _redactorProvider = Argument.IsNotNull(redactorProvider);

    /// <inheritdoc />
    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue result
    )
    {
        var shape = _Shapes.GetOrAdd(value.GetType(), _CreateShape);

        if (shape is null)
        {
            result = null!;
            return false;
        }

        result = shape.TypeClassifications is { } typeClassifications
            ? _Redact(value, typeClassifications)
            : _DestructureMembers(value, shape, propertyValueFactory);

        return true;
    }

    private StructureValue _DestructureMembers(object value, TypeShape shape, ILogEventPropertyValueFactory factory)
    {
        var properties = new List<LogEventProperty>(shape.Properties.Length);

        foreach (var member in shape.Properties)
        {
            if (!_TryGetValue(member.Property, value, out var propertyValue))
            {
                properties.Add(new LogEventProperty(member.Property.Name, new ScalarValue(propertyValue)));
                continue;
            }

            var logValue = member.Classifications is { } classifications
                ? _Redact(propertyValue, classifications)
                : factory.CreatePropertyValue(propertyValue, destructureObjects: true);

            properties.Add(new LogEventProperty(member.Property.Name, logValue));
        }

        return new StructureValue(properties, shape.TypeTag);
    }

    private LogEventPropertyValue _Redact(object? value, DataClassificationSet classifications)
    {
        if (value is null)
        {
            return new ScalarValue(value: null);
        }

        var redactor = _redactorProvider.GetRedactor(classifications);

        if (value is IEnumerable sequence and not string)
        {
            var elements = new List<LogEventPropertyValue>();

            foreach (var element in sequence)
            {
                elements.Add(new ScalarValue(element is null ? null : _RedactScalar(redactor, element)));
            }

            return new SequenceValue(elements);
        }

        return new ScalarValue(_RedactScalar(redactor, value));
    }

    private static string _RedactScalar(Redactor redactor, object value)
    {
        // Invariant culture keeps a pseudonym stable across hosts: the same number or date must hash the same way
        // whatever the server's culture.
        return redactor.Redact(value, format: null, CultureInfo.InvariantCulture);
    }

    // Mirrors Serilog's PropertyValueConverter: a throwing getter becomes a message naming the exception type, so
    // one bad property does not drop the whole event.
    private static bool _TryGetValue(PropertyInfo property, object instance, out object? value)
    {
        try
        {
            value = property.GetValue(instance);
            return true;
        }
        catch (TargetInvocationException ex)
        {
            SelfLog.WriteLine("The property accessor {0} threw exception: {1}", property, ex);
            value = "The property accessor threw an exception: " + ex.InnerException?.GetType().Name;
            return false;
        }
        catch (NotSupportedException)
        {
            SelfLog.WriteLine("The property accessor {0} is not supported via Reflection API", property);
            value = "Accessing this property is not supported via Reflection API";
            return false;
        }
    }

    private static TypeShape? _CreateShape(Type type)
    {
        var typeClassifications = _ReadClassifications(type);

        if (typeClassifications is not null)
        {
            return new TypeShape(_TypeTag(type), typeClassifications, []);
        }

        var members = new List<MemberShape>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var anyClassified = false;

        // FlattenHierarchy plus most-derived-first ordering matches Serilog: a property hidden with `new` appears
        // once, as the derived declaration.
        var properties = type.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy
        );

        foreach (var property in properties.OrderByDescending(p => _InheritanceDepth(p.DeclaringType)))
        {
            if (property.GetMethod is not { IsPublic: true } || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (!seenNames.Add(property.Name))
            {
                continue;
            }

            var classifications = _ReadClassifications(property);
            anyClassified |= classifications is not null;
            members.Add(new MemberShape(property, classifications));
        }

        if (!anyClassified)
        {
            return null;
        }

        // Keep declaration order stable for readers: base-type properties first, as Serilog emits them.
        var ordered = members.OrderBy(m => _InheritanceDepth(m.Property.DeclaringType)).ToArray();

        return new TypeShape(_TypeTag(type), TypeClassifications: null, ordered);
    }

    private static DataClassificationSet? _ReadClassifications(MemberInfo member)
    {
        // Attribute.GetCustomAttributes, unlike MemberInfo.GetCustomAttributes, follows property overrides when
        // inherit is true. The attribute allows several instances, so read them all instead of expecting one.
        var attributes = Attribute.GetCustomAttributes(member, typeof(DataClassificationAttribute), inherit: true);

        List<DataClassification>? classifications = null;

        foreach (var attribute in attributes)
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

    // Serilog tags a structure with the type name, except for compiler-generated (anonymous) types.
    private static string? _TypeTag(Type type)
    {
        return type.Name.Length == 0 || type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
            ? null
            : type.Name;
    }

    private sealed record TypeShape(
        string? TypeTag,
        DataClassificationSet? TypeClassifications,
        MemberShape[] Properties
    );

    private sealed record MemberShape(PropertyInfo Property, DataClassificationSet? Classifications);
}
