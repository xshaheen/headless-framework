using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

// ReSharper disable UnusedMember.Global
#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Primitives;

/// <summary>
/// Provides a set of static methods for retrieving JSON converters for various data types.
/// </summary>
public static class JsonInternalConverters
{
    private static JsonConverter<Guid>? _guidConverter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="Guid"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<Guid> GuidConverter => _guidConverter ??= GetInternalConverter<Guid>();

    private static JsonConverter<string?>? _stringConverter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="string"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<string?> StringConverter => _stringConverter ??= GetInternalConverter<string?>();

    private static JsonConverter<bool>? _booleanConverter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="bool"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<bool> BooleanConverter => _booleanConverter ??= GetInternalConverter<bool>();

    private static JsonConverter<byte>? _byteConverter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="byte"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<byte> ByteConverter => _byteConverter ??= GetInternalConverter<byte>();

    private static JsonConverter<char>? _charConverter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="char"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<char> CharConverter => _charConverter ??= GetInternalConverter<char>();

    private static JsonConverter<TimeSpan>? _timeSpanConverter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="TimeSpan"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<TimeSpan> TimeSpanConverter => _timeSpanConverter ??= GetInternalConverter<TimeSpan>();

    private static JsonConverter<DateTime>? _dateTimeConverter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="DateTime"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<DateTime> DateTimeConverter => _dateTimeConverter ??= GetInternalConverter<DateTime>();

    private static JsonConverter<DateTimeOffset>? _dateTimeOffsetConverter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="DateTimeOffset"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<DateTimeOffset> DateTimeOffsetConverter =>
        _dateTimeOffsetConverter ??= GetInternalConverter<DateTimeOffset>();

    private static JsonConverter<DateOnly>? _dateOnlyConverter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="DateOnly"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<DateOnly> DateOnlyConverter => _dateOnlyConverter ??= GetInternalConverter<DateOnly>();

    private static JsonConverter<TimeOnly>? _timeOnlyConverter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="TimeOnly"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<TimeOnly> TimeOnlyConverter => _timeOnlyConverter ??= GetInternalConverter<TimeOnly>();

    private static JsonConverter<float>? _floatConverter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="float"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<float> FloatConverter => _floatConverter ??= GetInternalConverter<float>();

    private static JsonConverter<decimal>? _decimalConverter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="decimal"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<decimal> DecimalConverter => _decimalConverter ??= GetInternalConverter<decimal>();

    private static JsonConverter<double>? _doubleConverter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="double"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<double> DoubleConverter => _doubleConverter ??= GetInternalConverter<double>();

    private static JsonConverter<short>? _int16Converter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="short"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<short> Int16Converter => _int16Converter ??= GetInternalConverter<short>();

    private static JsonConverter<int>? _int32Converter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="int"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<int> Int32Converter => _int32Converter ??= GetInternalConverter<int>();

    private static JsonConverter<long>? _int64Converter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="long"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<long> Int64Converter => _int64Converter ??= GetInternalConverter<long>();

    private static JsonConverter<sbyte>? _sbyteConverter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="sbyte"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<sbyte> SByteConverter => _sbyteConverter ??= GetInternalConverter<sbyte>();

    private static JsonConverter<ushort>? _uint16Converter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="ushort"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<ushort> UInt16Converter => _uint16Converter ??= GetInternalConverter<ushort>();

    private static JsonConverter<uint>? _uint32Converter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="uint"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<uint> UInt32Converter => _uint32Converter ??= GetInternalConverter<uint>();

    private static JsonConverter<ulong>? _uint64Converter;

    /// <summary>Returns a <see cref="JsonConverter{T}"/> instance that converts <see cref="ulong"/> values.</summary>
    /// <remarks>This API is for use by the output of the System.Text.Json source generator and should not be called directly.</remarks>
    public static JsonConverter<ulong> UInt64Converter => _uint64Converter ??= GetInternalConverter<ulong>();

    internal static JsonConverter<T> GetInternalConverter<T>()
    {
        // Todo keep track and remove later.

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
