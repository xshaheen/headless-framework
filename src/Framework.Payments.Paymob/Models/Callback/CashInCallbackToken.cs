// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using System.Text.Json.Serialization;
using Framework.Payments.Paymob.CashIn.Internal;

namespace Framework.Payments.Paymob.CashIn.Models.Callback;

[PublicAPI]
public sealed class CashInCallbackToken
{
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("masked_pan")]
    public required string MaskedPan { get; init; }

    [JsonPropertyName("merchant_id")]
    public int MerchantId { get; init; }

    [JsonPropertyName("card_subtype")]
    public required string CardSubtype { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("order_id")]
    public required string OrderId { get; init; }

    [JsonPropertyName("user_added")]
    public bool UserAdded { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; init; }

    /// <summary>Return the concatenated string of transaction.</summary>
    public string ToConcatenatedString()
    {
        return CardSubtype
            + CreatedAt
            + Email
            + Id.ToString(CultureInfo.InvariantCulture)
            + MaskedPan
            + MerchantId.ToString(CultureInfo.InvariantCulture)
            + OrderId.ToString(CultureInfo.InvariantCulture)
            + Token;
    }

    public DateTimeOffset CreatedAtDateTimeOffset()
    {
        var dateTime = DateTime.Parse(CreatedAt, CultureInfo.InvariantCulture);

        // If not have time zone offset consider it cairo time.
        return dateTime.Kind is DateTimeKind.Unspecified
            ? new DateTimeOffset(
                dateTime,
                AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter.EgyptTimeZone.GetUtcOffset(dateTime)
            )
            : DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture);
    }
}
