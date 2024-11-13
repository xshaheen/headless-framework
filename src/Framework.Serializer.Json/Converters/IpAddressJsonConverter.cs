// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Framework.Serializer.Json.Converters;

public sealed class IpAddressJsonConverter : JsonConverter<IPAddress?>
{
    public override bool HandleNull => true;

    public override IPAddress? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var ip = reader.GetString();

        return ip is null ? null : IPAddress.Parse(ip);
    }

    public override void Write(Utf8JsonWriter writer, IPAddress? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();

            return;
        }

        writer.WriteStringValue(value.ToString());
    }
}
