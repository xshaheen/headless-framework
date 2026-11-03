// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Internal;

/// <summary>
/// Reads a Paymob amount that arrives as a JSON number or as a string such as <c>"N/A"</c>.
/// Apply it per property: any other token is a contract change, so it fails deserialization
/// instead of being replaced by a placeholder that hides the real value.
/// </summary>
internal sealed class AddNullableAmountConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString()!,
            JsonTokenType.Number => reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
            _ => throw new JsonException($"Expected a number or a string for an amount, got {reader.TokenType}."),
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
