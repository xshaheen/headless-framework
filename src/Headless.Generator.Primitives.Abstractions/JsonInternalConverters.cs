// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;

namespace Headless.Generator.Primitives;

/// <summary>
/// Retrieves internal JSON converters for primitive underlying data types.
/// </summary>
/// <remarks>Internal generator-emitted plumbing. Kept public for generated code execution.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class JsonInternalConverters
{
    /// <summary>Gets a JSON converter for <see cref="Guid"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<Guid> GuidConverter => field ??= GetInternalConverter<Guid>();

    /// <summary>Gets a JSON converter for nullable <see cref="string"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<string?> StringConverter => field ??= GetInternalConverter<string?>();

    /// <summary>Gets a JSON converter for <see cref="bool"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<bool> BooleanConverter => field ??= GetInternalConverter<bool>();

    /// <summary>Gets a JSON converter for <see cref="byte"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<byte> ByteConverter => field ??= GetInternalConverter<byte>();

    /// <summary>Gets a JSON converter for <see cref="char"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<char> CharConverter => field ??= GetInternalConverter<char>();

    /// <summary>Gets a JSON converter for <see cref="TimeSpan"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<TimeSpan> TimeSpanConverter => field ??= GetInternalConverter<TimeSpan>();

    /// <summary>Gets a JSON converter for <see cref="DateTime"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<DateTime> DateTimeConverter => field ??= GetInternalConverter<DateTime>();

    /// <summary>Gets a JSON converter for <see cref="DateTimeOffset"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<DateTimeOffset> DateTimeOffsetConverter =>
        field ??= GetInternalConverter<DateTimeOffset>();

    /// <summary>Gets a JSON converter for <see cref="DateOnly"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<DateOnly> DateOnlyConverter => field ??= GetInternalConverter<DateOnly>();

    /// <summary>Gets a JSON converter for <see cref="TimeOnly"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<TimeOnly> TimeOnlyConverter => field ??= GetInternalConverter<TimeOnly>();

    /// <summary>Gets a JSON converter for <see cref="float"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<float> FloatConverter => field ??= GetInternalConverter<float>();

    /// <summary>Gets a JSON converter for <see cref="decimal"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<decimal> DecimalConverter => field ??= GetInternalConverter<decimal>();

    /// <summary>Gets a JSON converter for <see cref="double"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<double> DoubleConverter => field ??= GetInternalConverter<double>();

    /// <summary>Gets a JSON converter for <see cref="short"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<short> Int16Converter => field ??= GetInternalConverter<short>();

    /// <summary>Gets a JSON converter for <see cref="int"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<int> Int32Converter => field ??= GetInternalConverter<int>();

    /// <summary>Gets a JSON converter for <see cref="long"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<long> Int64Converter => field ??= GetInternalConverter<long>();

    /// <summary>Gets a JSON converter for <see cref="sbyte"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<sbyte> SByteConverter => field ??= GetInternalConverter<sbyte>();

    /// <summary>Gets a JSON converter for <see cref="ushort"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<ushort> UInt16Converter => field ??= GetInternalConverter<ushort>();

    /// <summary>Gets a JSON converter for <see cref="uint"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<uint> UInt32Converter => field ??= GetInternalConverter<uint>();

    /// <summary>Gets a JSON converter for <see cref="ulong"/> values.</summary>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<ulong> UInt64Converter => field ??= JsonMetadataServices.UInt64Converter;

    internal static JsonConverter<T> GetInternalConverter<T>()
    {
        // Clear IsInternalConverter through reflection so the converter remains usable outside internal serialization contexts.

        var jsonConverterType = typeof(JsonConverter<>).MakeGenericType(typeof(T));

        var prop = typeof(JsonMetadataServices)
            .GetProperties(BindingFlags.Static | BindingFlags.Public)
            .First(x => x.PropertyType == jsonConverterType);

        var instance = (JsonConverter<T>)prop.GetValue(null)! ?? throw new JsonException("Cannot retrieve to value");

        var type = instance.GetType();

        var internalConverterProp =
            type.GetProperty("IsInternalConverter", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new JsonException("Cannot convert to value");

        internalConverterProp.SetMethod!.Invoke(instance, [false]);

        return instance;
    }
}
