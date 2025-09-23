using Framework.Generator.Primitives;

namespace Tests;

#pragma warning disable CA2225
public sealed class JsonInternalConvertersTests
{
    [Fact]
    public void should_serialize_strings()
    {
        // given
        var stringValue = new StringValue { Value = "test" };

        // when
        var json = JsonSerializer.Serialize(stringValue);

        // then
        json.Should().Be("\"test\"");
    }

    [Fact]
    public void should_deserialize_strings()
    {
        const string json = "\"test\"";

        // when
        var stringValue = JsonSerializer.Deserialize<StringValue>(json);

        // then
        stringValue!.Value.Should().Be("test");
    }

    [JsonConverter(typeof(StringValueJsonConverter))]
    public sealed class StringValue
    {
        public required string Value { get; init; }

        public static implicit operator StringValue(string operand)
        {
            return new StringValue { Value = operand };
        }

        public static implicit operator string(StringValue operand)
        {
            return operand.Value;
        }
    }

    public sealed class StringValueJsonConverter : JsonConverter<StringValue>
    {
        /// <inheritdoc/>
        public override StringValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return JsonInternalConverters.StringConverter.Read(ref reader, typeToConvert, options)!;
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, StringValue value, JsonSerializerOptions options)
        {
            JsonInternalConverters.StringConverter.Write(writer, (string)value, options);
        }

        /// <inheritdoc/>
        public override StringValue ReadAsPropertyName(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            return JsonInternalConverters.StringConverter.ReadAsPropertyName(ref reader, typeToConvert, options)!;
        }

        /// <inheritdoc/>
        public override void WriteAsPropertyName(
            Utf8JsonWriter writer,
            StringValue value,
            JsonSerializerOptions options
        )
        {
            JsonInternalConverters.StringConverter.WriteAsPropertyName(writer, (string)value, options);
        }
    }
}
