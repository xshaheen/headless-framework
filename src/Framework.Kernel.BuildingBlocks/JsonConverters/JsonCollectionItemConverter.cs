using System.Text.Json;
using System.Text.Json.Serialization;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

/// <summary>Json collection converter.</summary>
/// <typeparam name="TDatatype">Type of item to convert.</typeparam>
/// <typeparam name="TConverterType">Converter to use for individual items.</typeparam>
public sealed class JsonCollectionItemConverter<TDatatype, TConverterType> : JsonConverter<IEnumerable<TDatatype>?>
    where TConverterType : JsonConverter
{
    public override bool HandleNull => true;

    public override IEnumerable<TDatatype>? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        JsonSerializerOptions serializerOptions = new(options);
        serializerOptions.Converters.Clear();
        serializerOptions.Converters.Add(Activator.CreateInstance<TConverterType>());

        List<TDatatype> returnValue = [];

        while (reader.TokenType is not JsonTokenType.EndArray)
        {
            if (reader.TokenType is not JsonTokenType.StartArray)
            {
                returnValue.Add(
                    (TDatatype)JsonSerializer.Deserialize(ref reader, typeof(TDatatype), serializerOptions)!
                );
            }

            reader.Read();
        }

        return returnValue;
    }

    public override void Write(Utf8JsonWriter writer, IEnumerable<TDatatype>? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();

            return;
        }

        JsonSerializerOptions serializerOptions = new(options);
        serializerOptions.Converters.Clear();
        serializerOptions.Converters.Add(Activator.CreateInstance<TConverterType>());

        writer.WriteStartArray();

        foreach (var data in value)
        {
            JsonSerializer.Serialize(writer, data, serializerOptions);
        }

        writer.WriteEndArray();
    }
}
