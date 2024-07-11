using System.Text.Json;
using System.Text.Json.Serialization;
using NetTools;

namespace Framework.BuildingBlocks.JsonConverters;

public sealed class IpAddressRangeJsonConverter : JsonConverter<IPAddressRange?>
{
    public override bool HandleNull => true;

    public override IPAddressRange? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var ip = reader.GetString();

        return ip is null ? null : IPAddressRange.Parse(ip);
    }

    public override void Write(Utf8JsonWriter writer, IPAddressRange? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();

            return;
        }

        writer.WriteStringValue(value.ToString());
    }
}
