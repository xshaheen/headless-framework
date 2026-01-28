// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Generator.Primitives;

/// <summary>
/// Provides a set of static methods for retrieving JSON converters for various data types.
/// </summary>
public static class JsonInternalConverters
{
    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="Guid"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<Guid> GuidConverter => field ??= GetInternalConverter<Guid>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="string"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<string?> StringConverter => field ??= GetInternalConverter<string?>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="bool"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<bool> BooleanConverter => field ??= GetInternalConverter<bool>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="byte"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<byte> ByteConverter => field ??= GetInternalConverter<byte>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="char"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<char> CharConverter => field ??= GetInternalConverter<char>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="TimeSpan"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<TimeSpan> TimeSpanConverter => field ??= GetInternalConverter<TimeSpan>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="DateTime"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<DateTime> DateTimeConverter => field ??= GetInternalConverter<DateTime>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="DateTimeOffset"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<DateTimeOffset> DateTimeOffsetConverter =>
        field ??= GetInternalConverter<DateTimeOffset>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="DateOnly"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<DateOnly> DateOnlyConverter => field ??= GetInternalConverter<DateOnly>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="TimeOnly"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<TimeOnly> TimeOnlyConverter => field ??= GetInternalConverter<TimeOnly>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="float"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<float> FloatConverter => field ??= GetInternalConverter<float>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="decimal"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<decimal> DecimalConverter => field ??= GetInternalConverter<decimal>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="double"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<double> DoubleConverter => field ??= GetInternalConverter<double>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="short"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<short> Int16Converter => field ??= GetInternalConverter<short>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="int"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<int> Int32Converter => field ??= GetInternalConverter<int>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="long"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<long> Int64Converter => field ??= GetInternalConverter<long>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="sbyte"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<sbyte> SByteConverter => field ??= GetInternalConverter<sbyte>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="ushort"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<ushort> UInt16Converter => field ??= GetInternalConverter<ushort>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="uint"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<uint> UInt32Converter => field ??= GetInternalConverter<uint>();

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="ulong"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    [field: MaybeNull, AllowNull]
    public static JsonConverter<ulong> UInt64Converter => field ??= JsonMetadataServices.UInt64Converter;

    internal static JsonConverter<T> GetInternalConverter<T>()
    {
        // SET JsonConverter<T>.IsInternalConverter to false (via reflection) to avoid any issues

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
