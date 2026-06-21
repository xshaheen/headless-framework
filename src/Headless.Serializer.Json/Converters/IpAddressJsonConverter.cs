// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Headless.Serializer.Converters;

/// <summary>
/// Converts <see cref="IPAddress"/> to and from its string representation (e.g., <c>"192.168.1.1"</c>
/// or <c>"::1"</c>). Both IPv4 and IPv6 addresses are supported.
/// </summary>
/// <remarks>
/// This converter is included in <see cref="JsonConstants.DefaultWebJsonOptions"/> and
/// <see cref="JsonConstants.DefaultInternalJsonOptions"/> by default via <c>JsonConstants._AddDefaultConverters</c>.
/// </remarks>
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
